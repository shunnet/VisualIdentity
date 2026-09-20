// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2025 Niklas Swärd
// https://github.com/NickSwardh/YoloDotNet

namespace YoloDotNet.Video.Services
{
    internal static class FrameSaveService
    {
        private static readonly BlockingCollection<(byte[] frameBytes, string fileName)> _frameQueue;
        private static CancellationTokenSource _cancellationTokenSource = default!;
        private static Task _backgroundTask = default!;
        private static readonly object _lifecycleLock = new();
        private static Exception? _lastError;
        private static bool _isRunning;

        static FrameSaveService()
        {
            _frameQueue = new BlockingCollection<(byte[] frameBytes, string fileName)>(100);
        }

        /// <summary>
        /// Add SKBitmap to queue
        /// </summary>
        /// <param name="image"></param>
        /// <param name="fileName"></param>
        /// <param name="format"></param>
        /// <param name="quality"></param>
        public static void AddToQueue(SKBitmap image,
            string fileName,
            SKEncodedImageFormat format = SKEncodedImageFormat.Jpeg,
            int quality = 100)
        {
            using var memoryStream = new MemoryStream();

            image.Encode(memoryStream, format, quality);
            byte[] encodedBytes = memoryStream.ToArray();

            Enqueue(encodedBytes, fileName);
        }

        /// <summary>
        /// Add SKImage to queue
        /// </summary>
        /// <param name="image"></param>
        /// <param name="fileName"></param>
        /// <param name="format"></param>
        /// <param name="quality"></param>
        public static void AddToQueue(SKImage image,
            string fileName,
            SKEncodedImageFormat format = SKEncodedImageFormat.Jpeg,
            int quality = 100)
        {
            using var memoryStream = new MemoryStream();

            using var imageData = image.Encode(format, quality);
            imageData.SaveTo(memoryStream);

            byte[] encodedBytes = memoryStream.ToArray();

            Enqueue(encodedBytes, fileName);
        }

        public static void Start()
        {
            lock (_lifecycleLock)
            {
                if (_isRunning)
                    return;

                _lastError = null;
                _cancellationTokenSource = new CancellationTokenSource();
                _isRunning = true;
                var owner = _cancellationTokenSource;
                _backgroundTask = Task.Run(() => ProcessQueue(owner), owner.Token);
            }
        }

        private static void Stop()
        {
            Task? task;
            CancellationTokenSource? cancellation;
            lock (_lifecycleLock)
            {
                if (!_isRunning)
                    return;

                task = _backgroundTask;
                cancellation = _cancellationTokenSource;
                cancellation.Cancel();
            }

            try
            {
                if (!task.Wait(TimeSpan.FromSeconds(5)))
                    Volatile.Write(ref _lastError, new TimeoutException("Timed out while stopping the frame-save worker."));
                else
                    while (_frameQueue.TryTake(out _)) { }
            }
            catch (AggregateException ex) when (ex.InnerExceptions.All(error => error is OperationCanceledException))
            {
                // Cancellation is the expected shutdown path.
            }
        }

        private static void ProcessQueue(CancellationTokenSource owner)
        {
            try
            {
                foreach (var (imageBytes, fileName) in _frameQueue.GetConsumingEnumerable(owner.Token))
                {
                    WriteFrameToFile(fileName, imageBytes);
                }
            }
            catch (OperationCanceledException)
            {
                // Exit gracefully.
            }
            catch (Exception ex)
            {
                lock (_lifecycleLock)
                {
                    if (ReferenceEquals(_cancellationTokenSource, owner))
                        _lastError = ex;
                }
            }
            finally
            {
                lock (_lifecycleLock)
                {
                    if (ReferenceEquals(_cancellationTokenSource, owner))
                    {
                        _isRunning = false;
                        _cancellationTokenSource = default!;
                    }
                }

                owner.Dispose();
            }
        }

        /// <summary>Queues an encoded frame without allowing an unhealthy worker to block inference indefinitely.</summary>
        private static void Enqueue(byte[] encodedBytes, string fileName)
        {
            lock (_lifecycleLock)
            {
                if (_lastError is not null)
                    throw new YoloDotNetException("The frame-save worker stopped after an I/O failure.", _lastError);
                if (!_isRunning)
                    throw new InvalidOperationException("The frame-save service is not running.");
            }

            if (!_frameQueue.TryAdd((encodedBytes, fileName), millisecondsTimeout: 100))
                throw new YoloDotNetException("The frame-save queue is full.");
        }

        /// <summary>Writes one encoded frame and truncates any previous file contents.</summary>
        internal static void WriteFrameToFile(string fileName, ReadOnlySpan<byte> imageBytes)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(fileName);

            using var fileStream = new FileStream(
                fileName,
                FileMode.Create,
                FileAccess.Write,
                FileShare.Read,
                4096,
                FileOptions.SequentialScan);

            fileStream.Write(imageBytes);
        }

        public static void DisposeStaticFrameQueue()
        {
            Stop();
        }
    }
}
