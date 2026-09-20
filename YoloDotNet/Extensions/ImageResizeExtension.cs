// SPDX-License-Identifier: MIT
// SPDX-FileCopyrightText: 2023-2026 Niklas Swärd
// https://github.com/NickSwardh/YoloDotNet

namespace YoloDotNet.Extensions
{
    /// <summary>Provides image resize and tensor-normalization operations.</summary>
    public static class ImageResizeExtension
    {
        /// <summary>
        /// Resizes the input image to the target dimensions by stretching it to fit the model input size, returning a pointer to RGB888x pixel data and the new dimensions.
        /// </summary>
        /// <remarks>
        /// This method is intended for models trained on stretched (non-aspect-ratio-preserving) datasets.
        /// Using this with models trained on letterbox/proportional preprocessing may reduce inference accuracy.
        /// For standard models, use <see cref="ResizeImageProportional{T}"/> instead.
        /// </remarks>
        /// <param name="img">The original image to resize.</param>
        /// <param name="samplingOptions">Sampling options used during resizing.</param>
        /// <param name="pinnedMemoryBuffer">A pinned memory buffer where the resized image will be written.</param>
        /// <param name="roi">Optional region of interest to crop before resizing.</param>
        /// <returns>The dimensions of the input image (or ROI if specified), required for bounding box scaling.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static SKSizeI ResizeImageStretched<T>(this T img, SKSamplingOptions samplingOptions, PinnedMemoryBuffer pinnedMemoryBuffer, SKRectI? roi = null)
        {
            ArgumentNullException.ThrowIfNull(img);
            ArgumentNullException.ThrowIfNull(pinnedMemoryBuffer);

            SKImage image = default!;
            var createdImage = false;

            if (img is SKImage skImage)
            {
                ValidateRoi(roi, skImage.Width, skImage.Height);
                image = roi.HasValue
                    ? YoloCore.CropToRoi(skImage, (SKRectI)roi)
                    : skImage;
            }
            else if (img is SKBitmap skBitmap)
            {
                ValidateRoi(roi, skBitmap.Width, skBitmap.Height);
                image = roi.HasValue
                    ? YoloCore.CropToRoi(skBitmap, (SKRectI)roi)
                    : SKImage.FromPixels(skBitmap.Info, skBitmap.GetPixels());

                createdImage = true;
            }
            else
            {
                throw new ArgumentException("Only SKImage and SKBitmap inputs are supported.", nameof(img));
            }

            try
            {
                int modelWidth = pinnedMemoryBuffer.ImageInfo.Width;
                int modelHeight = pinnedMemoryBuffer.ImageInfo.Height;
                int width = image.Width;
                int height = image.Height;

            // Stretch the image to fit the model input size regardless of aspect ratio and cropped ROI.
            // This may distort the image but ensures it matches the model's expected input dimensions.
            var srcRect = new SKRect(0, 0, image.Width, image.Height);
            var destRect = new SKRect(0, 0, modelWidth, modelHeight);

                pinnedMemoryBuffer.Canvas.DrawImage(image, srcRect, destRect, samplingOptions);
                return new SKSizeI(width, height);
            }
            finally
            {
                if (createdImage || roi.HasValue)
                    image.Dispose();
            }
        }

        /// <summary>
        /// Resizes the input image proportionally to fit the model input size, with RGB888x format and padded borders, returning a pointer to the pixel data and the new image dimensions.
        /// </summary>
        /// <param name="img">The original image to resize.</param>
        /// <param name="samplingOptions">Sampling options used during resizing.</param>
        /// <param name="pinnedMemoryBuffer">A pinned memory buffer where the resized image will be written.</param>
        /// <param name="roi">Optional region of interest to crop before resizing.</param>
        /// <returns>The dimensions of the input image (or ROI if specified), required for bounding box scaling.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static SKSizeI ResizeImageProportional<T>(this T img, SKSamplingOptions samplingOptions, PinnedMemoryBuffer pinnedMemoryBuffer, SKRectI? roi = null)
        {
            ArgumentNullException.ThrowIfNull(img);
            ArgumentNullException.ThrowIfNull(pinnedMemoryBuffer);

            SKImage image = default!;
            var createdImage = false;

            if (img is SKImage skImage)
            {
                ValidateRoi(roi, skImage.Width, skImage.Height);
                image = roi.HasValue
                    ? YoloCore.CropToRoi(skImage, (SKRectI)roi)
                    : skImage;
            }
            else if (img is SKBitmap skBitmap)
            {
                ValidateRoi(roi, skBitmap.Width, skBitmap.Height);
                image = roi.HasValue
                    ? YoloCore.CropToRoi(skBitmap, (SKRectI)roi)
                    : SKImage.FromPixels(skBitmap.Info, skBitmap.GetPixels());

                createdImage = true;
            }
            else
            {
                throw new ArgumentException("Only SKImage and SKBitmap inputs are supported.", nameof(img));
            }

            try
            {
                int modelWidth = pinnedMemoryBuffer.ImageInfo.Width;
                int modelHeight = pinnedMemoryBuffer.ImageInfo.Height;
                int width = image.Width;
                int height = image.Height;

            // If the image is smaller than the model input size, we can draw it directly onto the pinned memory buffer canvas without resizing, which avoids unnecessary resampling and preserves image quality.
            if (width < modelWidth && height < modelHeight)
            {
                int x = (modelWidth - width) / 2;
                int y = (modelHeight - height) / 2;
                var srcRect = new SKRect(0, 0, width, height);
                var dstRect = new SKRect(x, y, x + width, y + height);
                pinnedMemoryBuffer.Canvas.DrawImage(image, srcRect, dstRect, samplingOptions);
            }
            else
            {
                // Calculate the new image size based on the aspect ratio
                float scaleFactor = Math.Min((float)modelWidth / width, (float)modelHeight / height);

                // Use integer rounding instead of Math.Round
                int newWidth = (int)((width * scaleFactor) + 0.5f);
                int newHeight = (int)((height * scaleFactor) + 0.5f);

                // Calculate the destination rectangle within the model dimensions
                int x = (modelWidth - newWidth) / 2;
                int y = (modelHeight - newHeight) / 2;

                var srcRect = new SKRect(0, 0, width, height);
                var dstRect = new SKRect(x, y, x + newWidth, y + newHeight);

                // Draw the resized image onto the pinned memory buffer canvas as RGB888x with padding
                pinnedMemoryBuffer.Canvas.DrawImage(image, srcRect, dstRect, samplingOptions);
            }

                return new SKSizeI(width, height);
            }
            finally
            {
                if (createdImage || roi.HasValue)
                    image.Dispose();
            }
        }

        /// <summary>Validates that an optional region is non-empty and fully contained in the source.</summary>
        private static void ValidateRoi(SKRectI? roi, int imageWidth, int imageHeight)
        {
            if (roi is not { } value)
                return;

            if (value.Width <= 0 || value.Height <= 0 || value.Left < 0 || value.Top < 0 ||
                value.Right > imageWidth || value.Bottom > imageHeight)
            {
                throw new ArgumentOutOfRangeException(nameof(roi), "The ROI must be non-empty and fully contained in the source image.");
            }
        }

        /// <summary>
        /// Converts raw pixel image data to a normalized float array for model input.
        /// </summary>
        /// <param name="pixelsPtr">A pointer to the raw pixel image data in memory.</param>
        /// <param name="inputShape">The shape of the input tensor.</param>
        /// <param name="tensorBufferSize">The size of the tensor buffer, which should be equal to the product of the input shape dimensions.</param>
        /// <param name="tensorArrayBuffer">A pre-allocated float array buffer to store the normalized pixel values.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        unsafe public static void NormalizePixelsToArray(this IntPtr pixelsPtr,
            long[] inputShape,
            int tensorBufferSize,
            float[] tensorArrayBuffer)
        {
            ValidateNormalizationArguments(pixelsPtr, inputShape, tensorBufferSize, tensorArrayBuffer);

            var colorChannels = (int)inputShape[1];
            var height = (int)inputShape[2];
            var width = (int)inputShape[3];
            int totalPixels = width * height;

            float inv255 = 1.0f / 255.0f;
            byte* src = (byte*)pixelsPtr;

            if (colorChannels == 1)
            {
                fixed (float* dst = tensorArrayBuffer)
                {
                    for (int i = 0; i < totalPixels; i++)
                        dst[i] = src[i] * inv255;
                }
            }
            else
            {
                fixed (float* dstR = tensorArrayBuffer)
                {
                    float* dstG = dstR + totalPixels;
                    float* dstB = dstG + totalPixels;
                    int srcIndex = 0;

                    for (int i = 0; i < totalPixels; i++, srcIndex += 4)
                    {
                        dstR[i] = src[srcIndex] * inv255;
                        dstG[i] = src[srcIndex + 1] * inv255;
                        dstB[i] = src[srcIndex + 2] * inv255;
                    }
                }
            }
        }

        /// <summary>
        /// Overload of NormalizePixelsToArray that converts raw pixel image data to a normalized half-precision float (ushort) array for model input.
        /// </summary>
        /// <param name="pixelsPtr"></param>
        /// <param name="inputShape"></param>
        /// <param name="tensorBufferSize"></param>
        /// <param name="tensorArrayBuffer"></param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        unsafe public static void NormalizePixelsToArray(this IntPtr pixelsPtr,
            long[] inputShape,
            int tensorBufferSize,
            ushort[] tensorArrayBuffer)
        {
            ValidateNormalizationArguments(pixelsPtr, inputShape, tensorBufferSize, tensorArrayBuffer);

            var colorChannels = (int)inputShape[1];
            var height = (int)inputShape[2];
            var width = (int)inputShape[3];
            int totalPixels = width * height;

            float inv255 = 1.0f / 255.0f;
            byte* src = (byte*)pixelsPtr;

            if (colorChannels == 1)
            {
                fixed (ushort* dst = tensorArrayBuffer)
                {
                    for (int i = 0; i < totalPixels; i++)
                        dst[i] = FloatToUshort(src[i] * inv255);
                }
            }
            else
            {
                fixed (ushort* dstR = tensorArrayBuffer)
                {
                    ushort* dstG = dstR + totalPixels;
                    ushort* dstB = dstG + totalPixels;
                    int srcIndex = 0;

                    for (int i = 0; i < totalPixels; i++, srcIndex += 4)
                    {
                        dstR[i] = FloatToUshort(src[srcIndex] * inv255);
                        dstG[i] = FloatToUshort(src[srcIndex + 1] * inv255);
                        dstB[i] = FloatToUshort(src[srcIndex + 2] * inv255);
                    }
                }
            }
        }

        /// <summary>Validates source, shape, and destination bounds before unsafe normalization.</summary>
        private static void ValidateNormalizationArguments<T>(IntPtr pixelsPtr, long[] inputShape, int tensorBufferSize, T[] tensorArrayBuffer)
        {
            ArgumentNullException.ThrowIfNull(inputShape);
            ArgumentNullException.ThrowIfNull(tensorArrayBuffer);

            if (pixelsPtr == IntPtr.Zero)
                throw new ArgumentException("The pixel buffer pointer cannot be zero.", nameof(pixelsPtr));
            if (inputShape.Length < 4)
                throw new ArgumentException("The input shape must contain batch, channel, height, and width dimensions.", nameof(inputShape));

            var channels = inputShape[1];
            if (channels is not (1 or 3))
                throw new ArgumentException("Only one-channel and three-channel model inputs are supported.", nameof(inputShape));

            var requiredSize = checked(channels * inputShape[2] * inputShape[3]);
            if (requiredSize <= 0 || requiredSize > int.MaxValue || tensorBufferSize != requiredSize)
                throw new ArgumentOutOfRangeException(nameof(tensorBufferSize), "The tensor buffer size does not match the input shape.");
            if (tensorArrayBuffer.Length < tensorBufferSize)
                throw new ArgumentException("The destination array is smaller than the tensor buffer size.", nameof(tensorArrayBuffer));
        }

        /// <summary>Converts a Float32 value to its IEEE 754 binary16 bit pattern.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static ushort FloatToUshort(float value)
            => BitConverter.HalfToUInt16Bits((Half)value);
    }
}
