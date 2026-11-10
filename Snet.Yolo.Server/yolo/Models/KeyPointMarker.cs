// SPDX-License-Identifier: MIT
// SPDX-FileCopyrightText: 2025 Niklas Swärd
// https://github.com/NickSwardh/YoloDotNet

using SkiaSharp;
using Snet.Yolo.Server.yolo.Configuration;

namespace Snet.Yolo.Server.yolo.Models
{
    /// <summary>
    /// Represents a mapping between keypoints and their connections.
    /// </summary>
    public record KeyPointMarker
    {
        /// <summary>
        /// Color associated with the keypoint.
        /// </summary>
        public string Color { get; init; } = default!;

        /// <summary>
        /// Defines connections between keypoints.
        /// </summary>
        public KeyPointConnection[] Connections { get; init; } = [];
    }
}
