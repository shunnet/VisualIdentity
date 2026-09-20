// SPDX-License-Identifier: MIT
// SPDX-FileCopyrightText: 2026 Niklas Swärd
// https://github.com/NickSwardh/YoloDotNet

namespace YoloDotNet.Models
{
    /// <summary>Represents a dense monocular depth map aligned to the source image.</summary>
    public sealed class DepthEstimation
    {
        private readonly float[] _depth;

        internal DepthEstimation(int width, int height, float[] depth, float minimumDepth, float maximumDepth)
        {
            Width = width;
            Height = height;
            _depth = depth;
            MinimumDepth = minimumDepth;
            MaximumDepth = maximumDepth;
        }

        /// <summary>Gets the depth-map width in pixels.</summary>
        public int Width { get; }

        /// <summary>Gets the depth-map height in pixels.</summary>
        public int Height { get; }

        /// <summary>Gets the row-major depth values in meters.</summary>
        public ReadOnlyMemory<float> Depth => _depth;

        /// <summary>Gets the minimum finite depth value in meters.</summary>
        public float MinimumDepth { get; }

        /// <summary>Gets the maximum finite depth value in meters.</summary>
        public float MaximumDepth { get; }

        /// <summary>Gets the depth value at a pixel coordinate.</summary>
        /// <param name="x">The horizontal coordinate.</param>
        /// <param name="y">The vertical coordinate.</param>
        /// <returns>The depth in meters.</returns>
        public float GetDepth(int x, int y)
        {
            ArgumentOutOfRangeException.ThrowIfNegative(x);
            ArgumentOutOfRangeException.ThrowIfNegative(y);
            if (x >= Width || y >= Height)
                throw new ArgumentOutOfRangeException(x >= Width ? nameof(x) : nameof(y));

            return _depth[checked(y * Width + x)];
        }
    }
}
