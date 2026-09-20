// SPDX-License-Identifier: MIT
// SPDX-FileCopyrightText: 2023-2026 Niklas Swärd
// https://github.com/NickSwardh/YoloDotNet

namespace YoloDotNet.Modules.Interfaces
{
    /// <summary>Defines an axis-aligned object-detection processing module.</summary>
    public interface IObjectDetectionModule : IModule
    {
        /// <summary>Detects objects in an image.</summary>
        /// <typeparam name="T">The supported image type.</typeparam>
        /// <param name="image">The input image.</param>
        /// <param name="confidence">The object confidence threshold.</param>
        /// <param name="pixelConfidence">Reserved pixel confidence for the common module contract.</param>
        /// <param name="iou">The overlap threshold used by non-maximum suppression.</param>
        /// <param name="roi">An optional source region to process.</param>
        /// <returns>The object detections.</returns>
        List<ObjectDetection> ProcessImage<T>(T image, double confidence, double pixelConfidence, double iou, SKRectI? roi = null);
    }
}
