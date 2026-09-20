// SPDX-License-Identifier: MIT
// SPDX-FileCopyrightText: 2026 Niklas Sward
// https://github.com/NickSwardh/YoloDotNet

namespace YoloDotNet.Extensions
{
    /// <summary>Provides image conversion and overlay helpers for dense inference results.</summary>
    public static class DenseResultImageExtensions
    {
        private static readonly SKColor[] DefaultPalette =
            Array.ConvertAll(YoloDotNetColors.Get(), SKColor.Parse);

        private static readonly SKColor[] HeatmapStops =
        [
            new SKColor(0, 0, 128),
            new SKColor(0, 128, 255),
            new SKColor(0, 255, 128),
            new SKColor(255, 255, 0),
            new SKColor(255, 0, 0)
        ];

        /// <summary>Creates an opaque color bitmap from a semantic class map.</summary>
        /// <param name="result">The source-aligned semantic segmentation result.</param>
        /// <param name="colors">
        /// Optional class colors. When omitted, the default YoloDotNet palette is used. Colors repeat when
        /// the model exposes more classes than the palette contains.
        /// </param>
        /// <returns>A new bitmap with one color per semantic class.</returns>
        public static SKBitmap ToBitmap(this SemanticSegmentation result, IReadOnlyList<SKColor>? colors = null)
        {
            ArgumentNullException.ThrowIfNull(result);
            var palette = ResolvePalette(colors);
            var bitmap = CreateBitmap(result.Width, result.Height);
            WriteSemanticPixels(bitmap, result.ClassMap.Span, palette);
            return bitmap;
        }

        /// <summary>Blends a semantic class map onto an image in place.</summary>
        /// <param name="image">The source image to modify.</param>
        /// <param name="result">A semantic segmentation result aligned to <paramref name="image"/>.</param>
        /// <param name="opacity">Overlay opacity from 0 (transparent) to 255 (opaque).</param>
        /// <param name="colors">Optional class colors. The default YoloDotNet palette is used when omitted.</param>
        public static void Draw(
            this SKBitmap image,
            SemanticSegmentation result,
            byte opacity = 128,
            IReadOnlyList<SKColor>? colors = null)
        {
            ArgumentNullException.ThrowIfNull(image);
            ArgumentNullException.ThrowIfNull(result);
            if (image.Width != result.Width || image.Height != result.Height)
            {
                throw new ArgumentException(
                    "The semantic class map dimensions must match the target image dimensions.",
                    nameof(result));
            }

            if (opacity == 0)
                return;

            using var overlay = result.ToBitmap(colors);
            using var canvas = new SKCanvas(image);
            using var paint = new SKPaint { Color = SKColors.White.WithAlpha(opacity), IsAntialias = false };
            canvas.DrawBitmap(overlay, 0, 0, ImageConfig.SegmentationResamplingOptions, paint);
        }

        /// <summary>Creates a normalized 8-bit grayscale visualization of a depth map.</summary>
        /// <param name="result">The source-aligned depth result.</param>
        /// <param name="invert">When true, nearer values are brighter; otherwise farther values are brighter.</param>
        /// <returns>A new display-ready bitmap. The depth values in <paramref name="result"/> are not modified.</returns>
        public static SKBitmap ToGrayscaleBitmap(this DepthEstimation result, bool invert = false)
        {
            ArgumentNullException.ThrowIfNull(result);
            var bitmap = CreateBitmap(result.Width, result.Height);
            WriteDepthPixels(bitmap, result, invert, useHeatmap: false);
            return bitmap;
        }

        /// <summary>Creates a normalized blue-to-red heatmap visualization of a depth map.</summary>
        /// <param name="result">The source-aligned depth result.</param>
        /// <param name="invert">When true, nearer values are red; otherwise farther values are red.</param>
        /// <returns>A new display-ready bitmap. The depth values in <paramref name="result"/> are not modified.</returns>
        public static SKBitmap ToHeatmapBitmap(this DepthEstimation result, bool invert = false)
        {
            ArgumentNullException.ThrowIfNull(result);
            var bitmap = CreateBitmap(result.Width, result.Height);
            WriteDepthPixels(bitmap, result, invert, useHeatmap: true);
            return bitmap;
        }

        private static SKBitmap CreateBitmap(int width, int height)
        {
            var bitmap = new SKBitmap(width, height, SKColorType.Bgra8888, SKAlphaType.Opaque);
            if (bitmap.GetPixels() == IntPtr.Zero)
            {
                bitmap.Dispose();
                throw new InvalidOperationException("Unable to allocate the result bitmap.");
            }

            return bitmap;
        }

        private static SKColor[] ResolvePalette(IReadOnlyList<SKColor>? colors)
        {
            if (colors is { Count: > 0 })
            {
                var supplied = new SKColor[colors.Count];
                for (var i = 0; i < supplied.Length; i++)
                    supplied[i] = colors[i];
                return supplied;
            }

            return DefaultPalette;
        }

        private static unsafe void WriteSemanticPixels(SKBitmap bitmap, ReadOnlySpan<byte> classMap, SKColor[] palette)
        {
            Span<SKColor> indexedPalette = stackalloc SKColor[byte.MaxValue + 1];
            for (var i = 0; i < indexedPalette.Length; i++)
                indexedPalette[i] = palette[i % palette.Length];

            var pixels = (byte*)bitmap.GetPixels().ToPointer();
            for (var y = 0; y < bitmap.Height; y++)
            {
                var row = pixels + y * bitmap.RowBytes;
                var sourceOffset = y * bitmap.Width;
                for (var x = 0; x < bitmap.Width; x++)
                {
                    var color = indexedPalette[classMap[sourceOffset + x]];
                    var offset = x * 4;
                    row[offset] = color.Blue;
                    row[offset + 1] = color.Green;
                    row[offset + 2] = color.Red;
                    row[offset + 3] = byte.MaxValue;
                }
            }
        }

        private static unsafe void WriteDepthPixels(
            SKBitmap bitmap,
            DepthEstimation result,
            bool invert,
            bool useHeatmap)
        {
            var range = result.MaximumDepth - result.MinimumDepth;
            var inverseRange = range > 0f ? 1f / range : 0f;
            var depth = result.Depth.Span;
            var pixels = (byte*)bitmap.GetPixels().ToPointer();

            for (var y = 0; y < bitmap.Height; y++)
            {
                var row = pixels + y * bitmap.RowBytes;
                var sourceOffset = y * bitmap.Width;
                for (var x = 0; x < bitmap.Width; x++)
                {
                    var normalized = inverseRange > 0f
                        ? Math.Clamp((depth[sourceOffset + x] - result.MinimumDepth) * inverseRange, 0f, 1f)
                        : 0f;
                    if (invert)
                        normalized = 1f - normalized;

                    var color = useHeatmap ? GetHeatmapColor(normalized) : GetGrayscaleColor(normalized);
                    var offset = x * 4;
                    row[offset] = color.Blue;
                    row[offset + 1] = color.Green;
                    row[offset + 2] = color.Red;
                    row[offset + 3] = byte.MaxValue;
                }
            }
        }

        private static SKColor GetGrayscaleColor(float value)
        {
            var channel = (byte)MathF.Round(value * byte.MaxValue);
            return new SKColor(channel, channel, channel);
        }

        private static SKColor GetHeatmapColor(float value)
        {
            var scaled = value * (HeatmapStops.Length - 1);
            var left = Math.Min((int)scaled, HeatmapStops.Length - 2);
            var amount = scaled - left;
            return new SKColor(
                Lerp(HeatmapStops[left].Red, HeatmapStops[left + 1].Red, amount),
                Lerp(HeatmapStops[left].Green, HeatmapStops[left + 1].Green, amount),
                Lerp(HeatmapStops[left].Blue, HeatmapStops[left + 1].Blue, amount));
        }

        private static byte Lerp(byte start, byte end, float amount)
            => (byte)MathF.Round(start + (end - start) * amount);
    }
}
