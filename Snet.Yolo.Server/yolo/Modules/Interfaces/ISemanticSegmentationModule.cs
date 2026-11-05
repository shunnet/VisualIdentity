// SPDX-License-Identifier: MIT
// SPDX-FileCopyrightText: 2026 Niklas Swärd
// https://github.com/NickSwardh/YoloDotNet

using SkiaSharp;
using Snet.Yolo.Server.yolo.Models;
namespace Snet.Yolo.Server.yolo.Modules.Interfaces
{
    /// <summary>Defines a dense semantic-segmentation processing module.</summary>
    internal interface ISemanticSegmentationModule : IModule
    {
        /// <summary>Assigns a semantic class to every source pixel.</summary>
        SemanticSegmentation ProcessImage<T>(T image, SKRectI? roi = null);
    }
}
