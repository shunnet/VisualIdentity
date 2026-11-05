// SPDX-License-Identifier: MIT
// SPDX-FileCopyrightText: 2023-2026 Niklas Swärd
// https://github.com/NickSwardh/YoloDotNet

using SkiaSharp;
using Snet.Yolo.Server.yolo.Core;
using Snet.Yolo.Server.yolo.Models;
using Snet.Yolo.Server.yolo.Modules.Interfaces;
using Snet.Yolo.Server.yolo.Modules.V8;
namespace Snet.Yolo.Server.yolo.Modules.V9
{
    internal class ObjectDetectionModuleV9 : IObjectDetectionModule
    {
        private readonly YoloCore _yoloCore;

        public OnnxModel OnnxModel => _yoloCore.OnnxModel;
        private readonly ObjectDetectionModuleV8 _objectDetectionModule = default!;

        public ObjectDetectionModuleV9(YoloCore yoloCore)
        {
            _yoloCore = yoloCore;

            // Yolov9 uses the YOLOv8 model architecture.
            _objectDetectionModule = new ObjectDetectionModuleV8(_yoloCore);
        }

        public List<ObjectDetection> ProcessImage<T>(T image, double confidence, double pixelConfidence, double iou, SKRectI? roi = null)
            => _objectDetectionModule.ProcessImage(image, confidence, pixelConfidence, iou, roi);

        public void Dispose()
        {
            _yoloCore?.Dispose();

            GC.SuppressFinalize(this);
        }

    }
}