// SPDX-License-Identifier: MIT
// SPDX-FileCopyrightText: 2026 Niklas Swärd
// https://github.com/NickSwardh/YoloDotNet

namespace YoloDotNet.Modules.V26
{
    /// <summary>Projects dense model outputs back to source-image coordinates.</summary>
    internal static class DenseOutputProjector
    {
        /// <summary>Projects categorical pixels with nearest-neighbor sampling.</summary>
        internal static byte[] ProjectNearest(
            ReadOnlySpan<byte> source,
            int sourceWidth,
            int sourceHeight,
            int destinationWidth,
            int destinationHeight,
            int modelWidth,
            int modelHeight,
            ImageResize resize,
            (float XPad, float YPad, float XGain, float YGain) gain)
        {
            ValidateArguments(source.Length, sourceWidth, sourceHeight, destinationWidth, destinationHeight, modelWidth, modelHeight);

            var destination = new byte[checked(destinationWidth * destinationHeight)];
            var xIndexes = ArrayPool<int>.Shared.Rent(destinationWidth);

            try
            {
                for (var x = 0; x < destinationWidth; x++)
                {
                    var sourceX = MapCoordinate(x, sourceWidth, modelWidth, destinationWidth, resize, gain.XPad, gain.XGain);
                    xIndexes[x] = Math.Clamp((int)MathF.Round(sourceX), 0, sourceWidth - 1);
                }

                for (var y = 0; y < destinationHeight; y++)
                {
                    var sourceY = MapCoordinate(y, sourceHeight, modelHeight, destinationHeight, resize, gain.YPad,
                        resize == ImageResize.Proportional ? gain.XGain : gain.YGain);
                    var sourceRow = Math.Clamp((int)MathF.Round(sourceY), 0, sourceHeight - 1) * sourceWidth;
                    var destinationRow = y * destinationWidth;

                    for (var x = 0; x < destinationWidth; x++)
                        destination[destinationRow + x] = source[sourceRow + xIndexes[x]];
                }

                return destination;
            }
            finally
            {
                ArrayPool<int>.Shared.Return(xIndexes);
            }
        }

        /// <summary>Projects continuous depth values with bilinear interpolation.</summary>
        internal static float[] ProjectBilinear(
            ReadOnlySpan<float> source,
            int sourceWidth,
            int sourceHeight,
            int destinationWidth,
            int destinationHeight,
            int modelWidth,
            int modelHeight,
            ImageResize resize,
            (float XPad, float YPad, float XGain, float YGain) gain)
        {
            ValidateArguments(source.Length, sourceWidth, sourceHeight, destinationWidth, destinationHeight, modelWidth, modelHeight);

            var destination = new float[checked(destinationWidth * destinationHeight)];
            var x0 = ArrayPool<int>.Shared.Rent(destinationWidth);
            var x1 = ArrayPool<int>.Shared.Rent(destinationWidth);
            var xWeight = ArrayPool<float>.Shared.Rent(destinationWidth);

            try
            {
                for (var x = 0; x < destinationWidth; x++)
                {
                    var sourceX = MapCoordinate(x, sourceWidth, modelWidth, destinationWidth, resize, gain.XPad, gain.XGain);
                    ResolveLinearCoordinate(sourceX, sourceWidth, out x0[x], out x1[x], out xWeight[x]);
                }

                for (var y = 0; y < destinationHeight; y++)
                {
                    var sourceY = MapCoordinate(y, sourceHeight, modelHeight, destinationHeight, resize, gain.YPad,
                        resize == ImageResize.Proportional ? gain.XGain : gain.YGain);
                    ResolveLinearCoordinate(sourceY, sourceHeight, out var y0, out var y1, out var yWeight);

                    var sourceRow0 = y0 * sourceWidth;
                    var sourceRow1 = y1 * sourceWidth;
                    var destinationRow = y * destinationWidth;

                    for (var x = 0; x < destinationWidth; x++)
                    {
                        var top = Lerp(source[sourceRow0 + x0[x]], source[sourceRow0 + x1[x]], xWeight[x]);
                        var bottom = Lerp(source[sourceRow1 + x0[x]], source[sourceRow1 + x1[x]], xWeight[x]);
                        destination[destinationRow + x] = Lerp(top, bottom, yWeight);
                    }
                }

                return destination;
            }
            finally
            {
                ArrayPool<int>.Shared.Return(x0);
                ArrayPool<int>.Shared.Return(x1);
                ArrayPool<float>.Shared.Return(xWeight);
            }
        }

        private static float MapCoordinate(
            int destinationCoordinate,
            int outputSize,
            int modelSize,
            int destinationSize,
            ImageResize resize,
            float padding,
            float gain)
        {
            var modelCoordinate = resize == ImageResize.Proportional
                ? (destinationCoordinate + 0.5f) / gain + padding
                : (destinationCoordinate + 0.5f) * gain;

            return modelCoordinate * outputSize / modelSize - 0.5f;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static float Lerp(float left, float right, float amount)
            => left + (right - left) * amount;

        private static void ResolveLinearCoordinate(float coordinate, int size, out int lower, out int upper, out float weight)
        {
            if (coordinate <= 0)
            {
                lower = upper = 0;
                weight = 0;
                return;
            }

            if (coordinate >= size - 1)
            {
                lower = upper = size - 1;
                weight = 0;
                return;
            }

            lower = (int)MathF.Floor(coordinate);
            upper = lower + 1;
            weight = coordinate - lower;
        }

        private static void ValidateArguments(
            int sourceLength,
            int sourceWidth,
            int sourceHeight,
            int destinationWidth,
            int destinationHeight,
            int modelWidth,
            int modelHeight)
        {
            if (sourceWidth <= 0 || sourceHeight <= 0 || destinationWidth <= 0 || destinationHeight <= 0 || modelWidth <= 0 || modelHeight <= 0)
                throw new ArgumentOutOfRangeException(nameof(sourceWidth), "Dense tensor and image dimensions must be positive.");
            if (sourceLength != checked(sourceWidth * sourceHeight))
                throw new YoloDotNetModelException("Dense output length does not match its declared tensor shape.");
        }
    }
}
