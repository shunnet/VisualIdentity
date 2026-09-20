// SPDX-License-Identifier: MIT
// SPDX-FileCopyrightText: 2025 Niklas Swärd
// https://github.com/NickSwardh/YoloDotNet

namespace YoloDotNet.Handlers
{
    /// <summary>
    /// A pool for managing reusable pinned memory buffers backed by SKBitmap instances.
    /// Helps reduce GC pressure and allocation cost in high-frequency image processing scenarios.
    /// </summary>
    public class PinnedMemoryBufferPool : IDisposable
    {
        // Internal thread-safe pool of reusable pinned memory buffers
        private readonly Stack<PinnedMemoryBuffer> _pool = [];
        private readonly HashSet<PinnedMemoryBuffer> _leased = [];

        // The image format/dimensions used to allocate each SKBitmap
        private readonly SKImageInfo _imageInfo;
        private readonly object _sync = new();
        private int _disposed;

        /// <summary>
        /// Initializes the buffer pool with a specified image layout and pre-allocates a number of buffers.
        /// </summary>
        public PinnedMemoryBufferPool(SKImageInfo skInfo, int initialSize = 2)
        {
            if (initialSize < 0)
                throw new ArgumentOutOfRangeException(nameof(initialSize));

            _imageInfo = skInfo;

            for (int i = 0; i < initialSize; i++)
                _pool.Push(new PinnedMemoryBuffer(_imageInfo));
        }

        /// <summary>
        /// Retrieves a buffer from the pool, or creates a new one if the pool is empty.
        /// </summary>
        public PinnedMemoryBuffer Rent()
        {
            lock (_sync)
            {
                ObjectDisposedException.ThrowIf(_disposed != 0, this);

                var buffer = _pool.Count > 0
                    ? _pool.Pop()
                    : new PinnedMemoryBuffer(_imageInfo);

                if (!_leased.Add(buffer))
                {
                    throw new InvalidOperationException("The buffer pool returned a buffer that is already leased.");
                }

                return buffer;
            }
        }

        /// <summary>
        /// Returns a used buffer back to the pool after clearing its contents.
        /// </summary>
        /// <param name="buffer">The buffer to be returned and reused.</param>
        public void Return(PinnedMemoryBuffer buffer)
        {
            ArgumentNullException.ThrowIfNull(buffer);

            lock (_sync)
            {
                if (!_leased.Remove(buffer))
                    throw new InvalidOperationException("The buffer was not rented from this pool or has already been returned.");

                if (_disposed != 0)
                {
                    buffer.Dispose();
                    return;
                }

                buffer.TargetBitmap.Erase(SKColors.Empty);
                _pool.Push(buffer);
            }
        }

        /// <summary>
        /// Releases all resources used by the pool.
        /// </summary>
        public void Dispose()
        {
            lock (_sync)
            {
                if (_disposed != 0)
                    return;
                _disposed = 1;

                while (_pool.Count > 0)
                    _pool.Pop().Dispose();
            }

            GC.SuppressFinalize(this);
        }
    }
}
