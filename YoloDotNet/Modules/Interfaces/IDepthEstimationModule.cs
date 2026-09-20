// SPDX-License-Identifier: MIT
// SPDX-FileCopyrightText: 2026 Niklas Swärd
// https://github.com/NickSwardh/YoloDotNet

namespace YoloDotNet.Modules.Interfaces
{
    /// <summary>Defines a monocular depth-estimation processing module.</summary>
    internal interface IDepthEstimationModule : IModule
    {
        /// <summary>Estimates a dense depth map for an image.</summary>
        DepthEstimation ProcessImage<T>(T image, SKRectI? roi = null);
    }
}
