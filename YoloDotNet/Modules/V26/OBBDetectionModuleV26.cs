// SPDX-License-Identifier: MIT
// SPDX-FileCopyrightText: 2026 Niklas Swärd
// https://github.com/NickSwardh/YoloDotNet

namespace YoloDotNet.Modules.V26
{
    internal class OBBDetectionModuleV26 : IOBBDetectionModule
    {
        private readonly YoloCore _yoloCore;
        private readonly OBBDetectionModuleV8? _rawModule;
        private readonly int _stride;

        public event EventHandler VideoProgressEvent = delegate { };
        public event EventHandler VideoCompleteEvent = delegate { };
        public event EventHandler VideoStatusEvent = delegate { };

        public OnnxModel OnnxModel => _yoloCore.OnnxModel;

        public OBBDetectionModuleV26(YoloCore yoloCore)
        {
            _yoloCore = yoloCore;

            // Get output shape from ONNX model. Format: [Batch, Attributes, Predictions]
            var outputShape = _yoloCore.OnnxModel.OutputShapes.ElementAt(0).Value;
            var outputLayout = Yolo26OutputLayoutResolver.Resolve(
                outputShape,
                _yoloCore.OnnxModel.Labels.Length + 5,
                7,
                "OBB");

            if (outputLayout == Yolo26OutputLayout.Raw)
            {
                _rawModule = new OBBDetectionModuleV8(_yoloCore);
                return;
            }

            var modelOutputElements = outputShape[2];

            _stride = modelOutputElements;

            // Override image resize to Proportional for OBB detection
            // OBB requires proportional resizing to maintain geometric validity
            _yoloCore.YoloOptions.ImageResize = ImageResize.Proportional;
        }

        public List<OBBDetection> ProcessImage<T>(T image, double confidence, double pixelConfidence, double iou, SKRectI? roi = null)
        {
            if (_rawModule is not null)
                return _rawModule.ProcessImage(image, confidence, pixelConfidence, iou, roi);

            using var inferenceResult = _yoloCore.Run(image, roi);
            var detections = ObjectDetection(inferenceResult, confidence, iou);

            return YoloCore.InferenceResultsToType(detections, roi, r => (OBBDetection)r);
        }

        #region Helper methods
        [MethodImpl(MethodImplOptions.AggressiveInlining)] // Inline this small method better performance
        public ObjectResult[] ObjectDetection(InferenceResult inferenceResult, double confidenceThreshold, double overlapThreshold)
        {
            var imageSize = inferenceResult.ImageOriginalSize;
            var ortSpan = inferenceResult.OrtSpan0;

            var (xPad, yPad, xGain, yGain) = _yoloCore.CalculateGain(imageSize);

            int validBoxCount = 0;
            var rowCount = ortSpan.Length / _stride;
            var boxes = ArrayPool<ObjectResult>.Shared.Rent(rowCount);

            try
            {

                for (var row = 0; row < rowCount; row++)
                {
                    var i = row * _stride;
                    // Confidence is at index 4
                    var confidence = ortSpan[i + 4];

                    // Early exit before reading other values
                    if (!float.IsFinite(confidence) || confidence < confidenceThreshold)
                        continue;

                    var x = ortSpan[i];
                    var y = ortSpan[i + 1];
                    var w = ortSpan[i + 2];
                    var h = ortSpan[i + 3];
                    var labelIndex = ortSpan[i + 5];
                    if (!float.IsInteger(labelIndex) || labelIndex < 0 || labelIndex >= _yoloCore.OnnxModel.Labels.Length)
                        continue;
                    var angle = ortSpan[i + 6];

                    int xMin, yMin, xMax, yMax;

                    var halfW = w / 2;
                    var halfH = h / 2;

                    // IMPORTANT:
                    // Preprocessing must use proportional (uniform) resizing.
                    // OBB output is only geometrically valid under proportional (uniform) resizing.
                    // Stretched resizing applies non-uniform scaling, turning rotated rectangles
                    // into sheared shapes and making (x, y, w, h, angle) representations invalid.

                    // The model outputs are center-x/center-y/width/height.
                    // Convert to corner coordinates using half width/height and apply padding+gain.
                    xMin = (int)((x - halfW - xPad) * xGain);
                    yMin = (int)((y - halfH - yPad) * xGain);
                    xMax = (int)((x + halfW - xPad) * xGain);
                    yMax = (int)((y + halfH - yPad) * xGain);

                    var boundingBox = new SKRectI(xMin, yMin, xMax, yMax);
                    var boundingBoxUnscaled = new SKRectI((int)x, (int)y, (int)w, (int)h);

                    boxes[validBoxCount++] = new ObjectResult
                    {
                        Label = _yoloCore.OnnxModel.Labels[(int)labelIndex],
                        Confidence = confidence,
                        BoundingBox = boundingBox,
                        BoundingBoxUnscaled = boundingBoxUnscaled,
                        BoundingBoxIndex = i,
                        OrientationAngle = angle
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
