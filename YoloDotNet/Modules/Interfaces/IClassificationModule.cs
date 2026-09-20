// SPDX-License-Identifier: MIT
// SPDX-FileCopyrightText: 2023-2025 Niklas Swärd
// https://github.com/NickSwardh/YoloDotNet

namespace YoloDotNet.Modules.Interfaces
{
    /// <summary>Defines an image-classification processing module.</summary>
    public interface IClassificationModule : IModule
    {
        /// <summary>Classifies an image and returns the highest-scoring classes.</summary>
        /// <typeparam name="T">The supported image type.</typeparam>
        /// <param name="image">The input image.</param>
        /// <param name="classes">The maximum number of classes to return.</param>
        /// <param name="pixelConfidence">Reserved confidence value for the common module contract.</param>
        /// <param name="iou">Reserved IoU value for the common module contract.</param>
        /// <returns>The classification results.</returns>
        List<Classification> ProcessImage<T>(T image, double classes, double pixelConfidence, double iou);
    }
}
