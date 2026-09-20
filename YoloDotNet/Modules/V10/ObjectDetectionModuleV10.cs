// SPDX-License-Identifier: MIT
// SPDX-FileCopyrightText: 2024-2026 Niklas Swärd
// https://github.com/NickSwardh/YoloDotNet

namespace YoloDotNet.Modules.V10
{
    internal class ObjectDetectionModuleV10 : IObjectDetectionModule
    {
        private readonly YoloCore _yoloCore;

        public event EventHandler VideoProgressEvent = delegate { };
        public event EventHandler VideoCompleteEvent = delegate { };
        public event EventHandler VideoStatusEvent = delegate { };

        public OnnxModel OnnxModel => _yoloCore.OnnxModel;

        public ObjectDetectionModuleV10(YoloCore yoloCore)
        {
            _yoloCore = yoloCore;

            // Get output shape from ONNX model. Format: [Batch, Attributes, Predictions]
            var outputShape = _yoloCore.OnnxModel.OutputShapes.ElementAt(0).Value;
            if (outputShape.Length != 3 || outputShape[2] != 6)
                throw new YoloDotNetModelException("YOLOv10 detection output must contain six values per detection.");

        }

        public List<ObjectDetection> ProcessImage<T>(T image, double confidence, double pixelConfidence, double iou, SKRectI? roi = null)
        {
            using var inferenceResult = _yoloCore.Run(image, roi);
            var detections = ObjectDetection(inferenceResult, confidence, iou);

            return YoloCore.InferenceResultsToType(detections, roi, r => (ObjectDetection)r);
        }

        #region Helper methods
        [MethodImpl(MethodImplOptions.AggressiveInlining)] // Inline this small method better performance
        private ObjectResult[] ObjectDetection(InferenceResult inferenceResult, double confidenceThreshold, double overlapThreshold)
        {
            var imageSize = inferenceResult.ImageOriginalSize;
            var ortSpan = inferenceResult.OrtSpan0;

            var (xPad, yPad, xGain, yGain) = _yoloCore.CalculateGain(imageSize);

            int validBoxCount = 0;
            const int stride = 6;
            var rowCount = ortSpan.Length / stride;
            var boxes = ArrayPool<ObjectResult>.Shared.Rent(rowCount);

            var width = imageSize.Width;
            var height = imageSize.Height;

            try
            {
                for (var row = 0; row < rowCount; row++)
                {
                    var i = row * stride;
                    var x = ortSpan[i];
                    var y = ortSpan[i + 1];
                    var w = ortSpan[i + 2];
                    var h = ortSpan[i + 3];
                    var confidence = ortSpan[i + 4];
                    var labelIndex = ortSpan[i + 5];

                    if (!float.IsFinite(confidence) || confidence < confidenceThreshold) continue;
                    if (!float.IsInteger(labelIndex) || labelIndex < 0 || labelIndex >= _yoloCore.OnnxModel.Labels.Length) continue;

                    int xMin, yMin, xMax, yMax;

                    if (_yoloCore.YoloOptions.ImageResize == ImageResize.Proportional)
                    {
                        xMin = (int)((x - xPad) * xGain);
                        yMin = (int)((y - yPad) * xGain);
                        xMax = (int)((w - xPad) * xGain);
                        yMax = (int)((h - yPad) * xGain);
                    }
                    else
                    {
                        // YOLOv10 exports corner coordinates in both resize modes.
                        xMin = Math.Clamp((int)(x / xGain), 0, width - 1);
                        yMin = Math.Clamp((int)(y / yGain), 0, height - 1);
                        xMax = Math.Clamp((int)(w / xGain), 0, width - 1);
                        yMax = Math.Clamp((int)(h / yGain), 0, height - 1);
                    }

                    boxes[validBoxCount++] = new ObjectResult
                    {
                        Label = _yoloCore.OnnxModel.Labels[(int)labelIndex],
                        Confidence = confidence,
                        BoundingBox = new SKRectI(xMin, yMin, xMax, yMax),
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
            _yoloCore?.Dispose();

            GC.SuppressFinalize(this);
        }

        #endregion
    }
}
