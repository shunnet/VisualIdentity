// SPDX-License-Identifier: MIT
// SPDX-FileCopyrightText: 2025 Niklas Swärd
// https://github.com/NickSwardh/YoloDotNet

namespace YoloDotNet.Core
{
    /// <summary>
    /// Represents the output tensors produced by one inference operation.
    /// </summary>
    /// <remarks>
    /// The result owns native or pooled resources. Consumers must dispose it after
    /// all tensor spans have been processed and must not retain those spans.
    /// </remarks>
    public ref struct InferenceResult
    {
        private readonly InferenceResultLease? _lease;
        private readonly ReadOnlySpan<float> _ortSpan0;
        private readonly ReadOnlySpan<float> _ortSpan1;
        private readonly ReadOnlySpan<byte> _ortByteSpan0;

        /// <summary>Gets the first model output tensor.</summary>
        public ReadOnlySpan<float> OrtSpan0
        {
            get
            {
                _lease?.ThrowIfDisposed();
                return _ortSpan0;
            }
        }

        /// <summary>Gets the optional second model output tensor.</summary>
        public ReadOnlySpan<float> OrtSpan1
        {
            get
            {
                _lease?.ThrowIfDisposed();
                return _ortSpan1;
            }
        }

        /// <summary>Gets the first model output when its tensor element type is UInt8.</summary>
        public ReadOnlySpan<byte> OrtByteSpan0
        {
            get
            {
                _lease?.ThrowIfDisposed();
                return _ortByteSpan0;
            }
        }

        /// <summary>Gets or sets the source image size used to scale model coordinates.</summary>
        public SKSizeI ImageOriginalSize { get; set; }

        /// <summary>Initializes an empty inference result.</summary>
        public InferenceResult()
        {
        }

        /// <summary>Initializes an inference result backed by the supplied output spans.</summary>
        /// <param name="ortSpan0">The first model output tensor.</param>
        /// <param name="ortSpan1">The optional second model output tensor.</param>
        /// <param name="owner">The resource owner that keeps the spans valid.</param>
        public InferenceResult(
            ReadOnlySpan<float> ortSpan0,
            ReadOnlySpan<float> ortSpan1,
            IDisposable? owner = null)
        {
            _ortSpan0 = ortSpan0;
            _ortSpan1 = ortSpan1;
            _lease = owner is null ? null : new InferenceResultLease(owner);
        }

        /// <summary>Initializes an inference result backed by a UInt8 output tensor.</summary>
        /// <param name="ortByteSpan0">The first model output tensor.</param>
        /// <param name="owner">The resource owner that keeps the span valid.</param>
        public InferenceResult(ReadOnlySpan<byte> ortByteSpan0, IDisposable? owner = null)
        {
            _ortByteSpan0 = ortByteSpan0;
            _lease = owner is null ? null : new InferenceResultLease(owner);
        }

        /// <summary>
        /// Releases the resources that keep the output tensor spans valid.
        /// </summary>
        public void Dispose()
        {
            _lease?.Dispose();
        }

        /// <summary>Shares disposal state across value-type copies of an inference result.</summary>
        private sealed class InferenceResultLease(IDisposable owner) : IDisposable
        {
            private IDisposable? _owner = owner;

            /// <summary>Throws when the output storage has already been released.</summary>
            public void ThrowIfDisposed()
                => ObjectDisposedException.ThrowIf(Volatile.Read(ref _owner) is null, nameof(InferenceResult));

            /// <summary>Releases the underlying output storage exactly once.</summary>
            public void Dispose()
                => Interlocked.Exchange(ref _owner, null)?.Dispose();
        }
    }
}
