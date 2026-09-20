// SPDX-License-Identifier: MIT
// SPDX-FileCopyrightText: 2026 Niklas Swärd
// https://github.com/NickSwardh/YoloDotNet

namespace YoloDotNet.Modules.V26
{
    internal class PoseEstimationModuleV26 : IPoseEstimationModule
    {
        private readonly YoloCore _yoloCore;
        private readonly PoseEstimationModuleV8? _rawModule;
        private int _dimensions;
        private int _keypointDimension;
        private int _totalKeyPoints;
        private int _stride;

        public event EventHandler VideoProgressEvent = delegate { };
        public event EventHandler VideoCompleteEvent = delegate { };
        public event EventHandler VideoStatusEvent = delegate { };

        public OnnxModel OnnxModel => _yoloCore.OnnxModel;

        public PoseEstimationModuleV26(YoloCore yoloCore)
        {
            _yoloCore = yoloCore;

            // Get output shape from ONNX model. Format: [Batch, Attributes, Predictions]
            var outputShape = _yoloCore.OnnxModel.OutputShapes.ElementAt(0).Value;
            var outputLayout = Yolo26OutputLayoutResolver.ResolveVariable(
                outputShape,
                4 + _yoloCore.OnnxModel.Labels.Length,
                6,
                3,
                "pose");

            if (outputLayout == Yolo26OutputLayout.Raw)
            {
                _rawModule = new PoseEstimationModuleV8(_yoloCore);
                return;
            }

            var modelOutputElements = outputShape[2];

            _stride = modelOutputElements;
            _dimensions = 6; // Subtract dimensions (x, y, w, h, confidence, labelIndex)
            _keypointDimension = 3; // Each keypoint has x, y, confidence

            // Calculate total keypoints
            var keyPointsSize = modelOutputElements - _dimensions;
            if (keyPointsSize < _keypointDimension || keyPointsSize % _keypointDimension != 0)
                throw new YoloDotNetModelException("The pose output must contain six detection values followed by XYZ keypoint triples.");
            _totalKeyPoints = keyPointsSize / _keypointDimension;
        }

        public List<PoseEstimation> ProcessImage<T>(T image, double confidence, double pixelConfidence, double iou, SKRectI? roi = null)
        {
            if (_rawModule is not null)
                return _rawModule.ProcessImage(image, confidence, pixelConfidence, iou, roi);

            using var inferenceResult = _yoloCore.Run(image, roi);
            var detections = ObjectDetection(inferenceResult, confidence, iou);

            return YoloCore.InferenceResultsToType(detections, roi, r => (PoseEstimation)r);
        }

        #region Helper methods
        [MethodImpl(MethodImplOptions.AggressiveInlining)] // Inline this small method better performance
        private ObjectResult[] ObjectDetection(InferenceResult inferenceResult, double confidenceThreshold, double overlapThreshold)
        {
            var imageSize = inferenceResult.ImageOriginalSize;
            var ortSpan = inferenceResult.OrtSpan0;

            var (xPad, yPad, xGain, yGain) = _yoloCore.CalculateGain(imageSize);

            int validBoxCount = 0;
            var rowCount = ortSpan.Length / _stride;
            var boxes = ArrayPool<ObjectResult>.Shared.Rent(rowCount);

            var keypointDataSize = _totalKeyPoints * _keypointDimension;

            try
            {
                for (var row = 0; row < rowCount; row++)
                {
                    var i = row * _stride;
                    var confidence = ortSpan[i + 4];
                    if (!float.IsFinite(confidence) || confidence < confidenceThreshold) continue;

                    var x = ortSpan[i];
                    var y = ortSpan[i + 1];
                    var w = ortSpan[i + 2];
                    var h = ortSpan[i + 3];
                    var labelIndex = ortSpan[i + 5];
                    if (!float.IsInteger(labelIndex) || labelIndex < 0 || labelIndex >= _yoloCore.OnnxModel.Labels.Length)
                        continue;

                    int xMin, yMin, xMax, yMax, keyPointIndex = 0;
                    var offset = i + _dimensions;
                    var keyPoints = new KeyPoint[_totalKeyPoints];

                    if (_yoloCore.YoloOptions.ImageResize == ImageResize.Proportional)
                    {
                        xMin = (int)((x - xPad) * xGain);
                        yMin = (int)((y - yPad) * xGain);
                        xMax = (int)((w - xPad) * xGain);
                        yMax = (int)((h - yPad) * xGain);

                        // Extract keypoints
                        for (int k = offset; k < keypointDataSize + offset; k += 3)
                        {
                            // Get keypoint coordinates and confidence and rescale to original image size
                            var keypointX = Math.Clamp((int)((ortSpan[k] - xPad) * xGain), 0, imageSize.Width - 1);
                            var keypointY = Math.Clamp((int)((ortSpan[k + 1] - yPad) * xGain), 0, imageSize.Height - 1);
                            var keypointConf = ortSpan[k + 2];

                            keyPoints[keyPointIndex++] = new KeyPoint(keypointX, keypointY, keypointConf);
                        }
                    }
                    else // Stretched
                    {
                        xMin = (int)(x / xGain);
                        yMin = (int)(y / yGain);
                        xMax = (int)(w / xGain);
                        yMax = (int)(h / yGain);

                        // Extract keypoints
                        for (int k = offset; k < keypointDataSize + offset; k += 3)
                        {
                            // Get keypoint coordinates and confidence and rescale to original image size
                            var keypointX = Math.Clamp((int)(ortSpan[k] / xGain), 0, imageSize.Width - 1);
                            var keypointY = Math.Clamp((int)(ortSpan[k + 1] / yGain), 0, imageSize.Height - 1);
                            var keypointConf = ortSpan[k + 2];

                            keyPoints[keyPointIndex++] = new KeyPoint(keypointX, keypointY, keypointConf);
                        }
                    }

                    var boundingBox = new SKRectI(xMin, yMin, xMax, yMax);
                    var boundingBoxUnscaled = new SKRectI((int)x, (int)y, (int)w, (int)h);

                    boxes[validBoxCount++] = new ObjectResult
                    {
                        Label = _yoloCore.OnnxModel.Labels[(int)labelIndex],
                        Confidence = confidence,
                        BoundingBox = boundingBox,
                        BoundingBoxUnscaled = boundingBoxUnscaled,
                        BoundingBoxIndex = i,
                        KeyPoints = keyPoints
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
