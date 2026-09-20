// SPDX-License-Identifier: MIT
// SPDX-FileCopyrightText: 2026 Niklas Swärd
// https://github.com/NickSwardh/YoloDotNet

namespace YoloDotNet.Models
{
    /// <summary>Represents a dense semantic class map aligned to the source image.</summary>
    public sealed class SemanticSegmentation
    {
        private readonly byte[] _classMap;

        internal SemanticSegmentation(int width, int height, byte[] classMap, IReadOnlyList<LabelModel> labels)
        {
            Width = width;
            Height = height;
            _classMap = classMap;
            Labels = labels;
        }

        /// <summary>Gets the class-map width in pixels.</summary>
        public int Width { get; }

        /// <summary>Gets the class-map height in pixels.</summary>
        public int Height { get; }

        /// <summary>Gets the row-major class index for every pixel.</summary>
        public ReadOnlyMemory<byte> ClassMap => _classMap;

        /// <summary>Gets the labels referenced by class-map values.</summary>
        public IReadOnlyList<LabelModel> Labels { get; }

        /// <summary>Gets the class index at a pixel coordinate.</summary>
        /// <param name="x">The horizontal coordinate.</param>
        /// <param name="y">The vertical coordinate.</param>
        /// <returns>The zero-based class index.</returns>
        public byte GetClassIndex(int x, int y)
        {
            ArgumentOutOfRangeException.ThrowIfNegative(x);
            ArgumentOutOfRangeException.ThrowIfNegative(y);
            if (x >= Width || y >= Height)
                throw new ArgumentOutOfRangeException(x >= Width ? nameof(x) : nameof(y));

            return _classMap[checked(y * Width + x)];
        }
    }
}
