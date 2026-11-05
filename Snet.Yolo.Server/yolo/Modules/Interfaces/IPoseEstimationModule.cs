// SPDX-License-Identifier: MIT
// SPDX-FileCopyrightText: 2023-2026 Niklas Swärd
// https://github.com/NickSwardh/YoloDotNet

using SkiaSharp;
using Snet.Yolo.Server.yolo.Models;
namespace Snet.Yolo.Server.yolo.Modules.Interfaces
{
    internal interface IPoseEstimationModule : IModule
    {
        List<PoseEstimation> ProcessImage<T>(T image, double confidence, double pixelConfidence, double iou, SKRectI? roi = null);
    }
}