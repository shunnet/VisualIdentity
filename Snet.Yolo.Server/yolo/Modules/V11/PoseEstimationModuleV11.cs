// SPDX-License-Identifier: MIT
// SPDX-FileCopyrightText: 2024-2026 Niklas Swärd
// https://github.com/NickSwardh/YoloDotNet

using SkiaSharp;
using Snet.Yolo.Server.yolo.Core;
using Snet.Yolo.Server.yolo.Models;
using Snet.Yolo.Server.yolo.Modules.Interfaces;
using Snet.Yolo.Server.yolo.Modules.V8;
namespace Snet.Yolo.Server.yolo.Modules.V11
{
    internal class PoseEstimationModuleV11 : IPoseEstimationModule
    {
        private readonly YoloCore _yoloCore;
        private readonly PoseEstimationModuleV8 _poseEstimationModuleV8 = default!;

        public OnnxModel OnnxModel => _yoloCore.OnnxModel;

        public PoseEstimationModuleV11(YoloCore yoloCore)
        {
            _yoloCore = yoloCore;

            // Yolov11 uses the YOLOv8 model architecture.
            _poseEstimationModuleV8 = new PoseEstimationModuleV8(_yoloCore);
        }

        public List<PoseEstimation> ProcessImage<T>(T image, double confidence, double pixelConfidence, double iou, SKRectI? roi = null)
            => _poseEstimationModuleV8.ProcessImage(image, confidence, pixelConfidence, iou, roi);

        #region Helper methods

        public void Dispose()
        {
            _poseEstimationModuleV8?.Dispose();
            _yoloCore?.Dispose();

            GC.SuppressFinalize(this);
        }

        #endregion
    }
}
