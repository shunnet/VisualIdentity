// SPDX-License-Identifier: MIT
// SPDX-FileCopyrightText: 2025 Niklas Swärd
// https://github.com/NickSwardh/YoloDotNet

namespace YoloDotNet.Handlers
{
    /// <summary>Owns a pinned pixel buffer and Skia drawing surface used for preprocessing.</summary>
    public class PinnedMemoryBuffer : IDisposable
    {
        private readonly byte[] _buffer;
        private readonly IntPtr _pointer;
        private readonly SKBitmap _targetBitmap;
        private readonly SKCanvas _canvas;
        private int _disposed;

        /// <summary>Gets the pixel format and dimensions of this buffer.</summary>
        public SKImageInfo ImageInfo { get; }

        /// <summary>Gets the managed backing array while the buffer is alive.</summary>
        public byte[] Buffer { get { ThrowIfDisposed(); return _buffer; } }

        /// <summary>Gets the pinned address while the buffer is alive.</summary>
        public IntPtr Pointer { get { ThrowIfDisposed(); return _pointer; } }

        /// <summary>Gets the bitmap that wraps the pinned buffer.</summary>
        public SKBitmap TargetBitmap { get { ThrowIfDisposed(); return _targetBitmap; } }

        /// <summary>Gets the drawing canvas for the target bitmap.</summary>
        public SKCanvas Canvas { get { ThrowIfDisposed(); return _canvas; } }

        private readonly GCHandle _handle;

        /// <summary>Allocates and pins a pixel buffer for the specified image format.</summary>
        /// <param name="imageInfo">The target image dimensions and pixel format.</param>
        public PinnedMemoryBuffer(SKImageInfo imageInfo)
        {
            ImageInfo = imageInfo;

            //var _imageInfo = new SKImageInfo(width, height, SKColorType.Rgb888x, SKAlphaType.Opaque);
            _buffer = new byte[imageInfo.BytesSize];

            _handle = GCHandle.Alloc(_buffer, GCHandleType.Pinned);
            _pointer = _handle.AddrOfPinnedObject();

            // Wrap the pinned buffer in a SKBitmap so we can draw into it
            _targetBitmap = new SKBitmap();

            try
            {
                if (!_targetBitmap.InstallPixels(imageInfo, _pointer, imageInfo.RowBytes))
                    throw new YoloDotNetException("Failed to install pixels into SKBitmap");

                _canvas = new SKCanvas(_targetBitmap);
            }
            catch
            {
                _targetBitmap.Dispose();
                if (_handle.IsAllocated)
                    _handle.Free();
                throw;
            }
        }

        /// <summary>Releases the Skia objects and pinned managed handle.</summary>
        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
                return;

            _canvas?.Dispose();
            _targetBitmap?.Dispose();

            if (_handle.IsAllocated)
                _handle.Free();

            GC.SuppressFinalize(this);
        }

        /// <summary>Releases the pinned handle if a consumer abandons the buffer without disposal.</summary>
        ~PinnedMemoryBuffer()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0 && _handle.IsAllocated)
                _handle.Free();
        }

        /// <summary>Throws when callers access native-backed members after disposal.</summary>
        private void ThrowIfDisposed()
            => ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
    }
}
