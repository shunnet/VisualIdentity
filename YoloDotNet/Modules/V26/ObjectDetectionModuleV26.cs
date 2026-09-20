// SPDX-License-Identifier: MIT
// SPDX-FileCopyrightText: 2026 Niklas Swärd
// https://github.com/NickSwardh/YoloDotNet

namespace YoloDotNet.Modules.V26
{
    internal class ObjectDetectionModuleV26 : IObjectDetectionModule
    {
        private readonly YoloCore _yoloCore;
        private readonly ObjectDetectionModuleV8? _rawModule;
        private readonly Yolo26OutputLayout _outputLayout;

        public event EventHandler VideoProgressEvent = delegate { };
        public event EventHandler VideoCompleteEvent = delegate { };
        public event EventHandler VideoStatusEvent = delegate { };

        public OnnxModel OnnxModel => _yoloCore.OnnxModel;

        public ObjectDetectionModuleV26(YoloCore yoloCore)
        {
            _yoloCore = yoloCore;

            // Get output shape from ONNX model. Format: [Batch, Attributes, Predictions]
            var outputShape = _yoloCore.OnnxModel.OutputShapes.ElementAt(0).Value;
            _outputLayout = Yolo26OutputLayoutResolver.Resolve(
                outputShape,
                _yoloCore.OnnxModel.Labels.Length + 4,
                6,
                "detection");

            if (_outputLayout == Yolo26OutputLayout.Raw)
                _rawModule = new ObjectDetectionModuleV8(_yoloCore);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)] // Inline this method better performance
        public List<ObjectDetection> ProcessImage<T>(T image, double confidence, double pixelConfidence, double iou = 0, SKRectI? roi = null)
        {
            if (_rawModule is not null)
                return _rawModule.ProcessImage(image, confidence, pixelConfidence, iou, roi);

            using var inferenceResult = _yoloCore.Run(image, roi);
            var detections = ObjectDetection(inferenceResult, confidence);

            return YoloCore.InferenceResultsToType(detections, roi, r => (ObjectDetection)r);
        }

        #region Helper methods
        [MethodImpl(MethodImplOptions.AggressiveInlining)] // Inline this method better performance
        public ObjectResult[] ObjectDetection(InferenceResult inferenceResult, double confidenceThreshold)
        {
            var imageSize = inferenceResult.ImageOriginalSize;
            var ortSpan = inferenceResult.OrtSpan0;

            var (xPad, yPad, xGain, yGain) = _yoloCore.CalculateGain(imageSize);

            int validBoxCount = 0;
            const int stride = 6;
            var rowCount = ortSpan.Length / stride;
            var boxes = ArrayPool<ObjectResult>.Shared.Rent(rowCount);

            try
            {
                for (var row = 0; row < rowCount; row++)
                {
                    var i = row * stride;
                    var confidence = ortSpan[i + 4];

                    // Filter out low confidence boxes
                    if (!float.IsFinite(confidence) || confidence < confidenceThreshold)
                        continue;

                    // Extract box data
                    var x = ortSpan[i];
                    var y = ortSpan[i + 1];
                    var w = ortSpan[i + 2];
                    var h = ortSpan[i + 3];
                    var labelIndex = ortSpan[i + 5];
                    if (!float.IsInteger(labelIndex) || labelIndex < 0 || labelIndex >= _yoloCore.OnnxModel.Labels.Length)
                        continue;

                    int xMin, yMin, xMax, yMax;

                    if (_yoloCore.YoloOptions.ImageResize == ImageResize.Proportional)
                    {
                        // Undo padding and rescale boxes to original image size
                        xMin = (int)((x - xPad) * xGain);
                        yMin = (int)((y - yPad) * xGain);
                        xMax = (int)((w - xPad) * xGain);
                        yMax = (int)((h - yPad) * xGain);
                    }
                    else
                    {
                        // Rescale boxes to original image size
                        xMin = (int)(x / xGain);
                        yMin = (int)(y / yGain);
                        xMax = (int)(w / xGain);
                        yMax = (int)(h / yGain);
                    }

                    var boundingBox = new SKRectI(xMin, yMin, xMax, yMax);
                    var boundingBoxUnscaled = new SKRectI((int)x, (int)y, (int)w, (int)h);

                    // Add box to results
                    boxes[validBoxCount++] = new ObjectResult
                    {
                        Label = _yoloCore.OnnxModel.Labels[(int)labelIndex],
                        Confidence = confidence,
                        BoundingBox = boundingBox,
                        BoundingBoxUnscaled = boundingBoxUnscaled,
                        BoundingBoxIndex = i
                    };
                }

                return boxes.AsSpan(0, validBoxCount).ToArray();
            }
            finally
            {
                ArrayPool<ObjectResult>.Shared.Return(boxes, clearArray: true);
            }
        }

        public void Dispose()
        {
            if (_rawModule is not null)
                _rawModule.Dispose();
            else
                _yoloCore.Dispose();

            GC.SuppressFinalize(this);
        }

        #endregion
    }
}
