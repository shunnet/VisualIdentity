// SPDX-License-Identifier: MIT
// SPDX-FileCopyrightText: 2023-2026 Niklas Swärd
// https://github.com/NickSwardh/YoloDotNet

namespace YoloDotNet.Modules.V8
{
    internal class PoseEstimationModuleV8 : IPoseEstimationModule
    {
        private readonly YoloCore _yoloCore;
        private readonly ObjectDetectionModuleV8 _objectDetectionModule;
        private int _modelOutputChannels;
        private int _totalKeypoints;

        public OnnxModel OnnxModel => _yoloCore.OnnxModel;

        public PoseEstimationModuleV8(YoloCore yoloCore)
        {
            _yoloCore = yoloCore;

            // Get output shape from ONNX model. Format: [Batch, Attributes, Predictions]
            var outputShape = _yoloCore.OnnxModel.OutputShapes.ElementAt(0).Value;
            if (outputShape.Length != 3)
                throw new YoloDotNetModelException("The pose output must have shape [batch, attributes, predictions].");

            var outputAttributes = outputShape[1];
            _modelOutputChannels = outputShape[2];

            var keypointValues = outputAttributes - 4 - _yoloCore.OnnxModel.Labels.Length;
            if (keypointValues < 3 || keypointValues % 3 != 0)
                throw new YoloDotNetModelException("The pose output must contain 4 box values, label scores, and XYZ keypoint triples.");
            _totalKeypoints = keypointValues / 3;

            _objectDetectionModule = new ObjectDetectionModuleV8(_yoloCore);
        }

        public List<PoseEstimation> ProcessImage<T>(T image, double confidence, double pixelConfidence, double iou, SKRectI? roi = null)
        {
            using var inferenceResult = _yoloCore.Run(image, roi);
            var detections = PoseEstimateImage(inferenceResult, confidence, iou);

            return YoloCore.InferenceResultsToType(detections, roi, r => (PoseEstimation)r);
        }

        #region Helper methods

        private ObjectResult[] PoseEstimateImage(InferenceResult inferenceResult, double threshold, double overlapThrehshold)
        {
            var boxes = _objectDetectionModule.ObjectDetection(inferenceResult, threshold, overlapThrehshold);

            var imageSize = inferenceResult.ImageOriginalSize;
            var ortSpan = inferenceResult.OrtSpan0;

            var (xPad, yPad, xGain, yGain) = _yoloCore.CalculateGain(imageSize);

            var labels = _yoloCore.OnnxModel.Labels.Length;

            var totalBoxes = boxes.Length;
            for (int i = 0; i < totalBoxes; i++)
            {
                var box = boxes[i];
                var poseEstimations = new KeyPoint[_totalKeypoints];
                var keypointOffset = box.BoundingBoxIndex + (_modelOutputChannels * (4 + labels)); // Skip boundingbox + labels (4 + labels) and move forward to the first keypoint

                for (var j = 0; j < _totalKeypoints; j++)
                {
                    var xIndex = keypointOffset;
                    var yIndex = xIndex + _modelOutputChannels;
                    var cIndex = yIndex + _modelOutputChannels;
                    keypointOffset += _modelOutputChannels * 3;

                    if ((uint)cIndex >= (uint)ortSpan.Length)
                        throw new YoloDotNetModelException("The pose output tensor is shorter than its declared shape.");

                    var x = 0;
                    var y = 0;

                    if (_yoloCore.YoloOptions.ImageResize == ImageResize.Proportional)
                    {
                        x = (int)((ortSpan[xIndex] - xPad) * xGain);
                        y = (int)((ortSpan[yIndex] - yPad) * xGain);
                    }
                    else
                    {
                        x = (int)(ortSpan[xIndex] / xGain);
                        y = (int)(ortSpan[yIndex] / yGain);
                    }

                    x = Math.Clamp(x, 0, imageSize.Width - 1);
                    y = Math.Clamp(y, 0, imageSize.Height - 1);

                    var confidence = ortSpan[cIndex];

                    poseEstimations[j] = new KeyPoint(x, y, confidence);
                }

                box.KeyPoints = poseEstimations;
            }

            return boxes;
        }

        public void Dispose()
        {
            _yoloCore?.Dispose();

            GC.SuppressFinalize(this);
        }

        #endregion
    }
}
