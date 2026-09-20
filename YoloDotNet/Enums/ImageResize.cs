// SPDX-License-Identifier: MIT
// SPDX-FileCopyrightText: 2024-2025 Niklas Swärd
// https://github.com/NickSwardh/YoloDotNet

namespace YoloDotNet.Enums
{
    /// <summary>Specifies how an input image is resized to the model dimensions.</summary>
    public enum ImageResize
    {
        /// <summary>Preserves aspect ratio and pads the unused area.</summary>
        Proportional,
        /// <summary>Stretches the image independently along each axis.</summary>
        Stretched
    }
}
