// SPDX-License-Identifier: MIT
// SPDX-FileCopyrightText: 2026 Niklas Swärd
// https://github.com/NickSwardh/YoloDotNet

namespace YoloDotNet.Modules.V26
{
    /// <summary>Processes YOLO26 dense semantic-segmentation outputs.</summary>
    internal sealed class SemanticSegmentationModuleV26 : ISemanticSegmentationModule
    {
        private readonly YoloCore _yoloCore;
        private readonly int _modelWidth;
        private readonly int _modelHeight;
        private readonly int _outputWidth;
        private readonly int _outputHeight;
        private readonly IReadOnlyList<LabelModel> _labels;

        public SemanticSegmentationModuleV26(YoloCore yoloCore)
        {
            _yoloCore = yoloCore;

            var inputShape = yoloCore.OnnxModel.InputShapes.Single().Value;
            var outputShape = yoloCore.OnnxModel.OutputShapes.Single().Value;
            if (outputShape.Length != 3 || outputShape[0] != 1 || outputShape[1] <= 0 || outputShape[2] <= 0)
                throw new YoloDotNetModelException("YOLO26 semantic output must have shape [1, height, width].");
            if (yoloCore.OnnxModel.Labels.Length is 0 or > 256)
                throw new YoloDotNetModelException("UInt8 semantic output requires between 1 and 256 labels.");

            _modelHeight = checked((int)inputShape[2]);
            _modelWidth = checked((int)inputShape[3]);
            _outputHeight = outputShape[1];
            _outputWidth = outputShape[2];
            _labels = Array.AsReadOnly(yoloCore.OnnxModel.Labels);
        }

        public OnnxModel OnnxModel => _yoloCore.OnnxModel;

        public SemanticSegmentation ProcessImage<T>(T image, SKRectI? roi = null)
        {
            using var inferenceResult = _yoloCore.Run(image, roi);
            var source = inferenceResult.OrtByteSpan0;
            if (source.IsEmpty)
                throw new YoloDotNetModelException("YOLO26 semantic output must use UInt8 class indexes.");

            foreach (var classIndex in source)
            {
                if (classIndex >= _labels.Count)
                    throw new YoloDotNetModelException($"Semantic output references unknown class index {classIndex}.");
            }

            var imageSize = inferenceResult.ImageOriginalSize;
            var classMap = DenseOutputProjector.ProjectNearest(
                source,
                _outputWidth,
                _outputHeight,
                imageSize.Width,
                imageSize.Height,
                _modelWidth,
                _modelHeight,
                _yoloCore.YoloOptions.ImageResize,
                _yoloCore.CalculateGain(imageSize));

            return new SemanticSegmentation(imageSize.Width, imageSize.Height, classMap, _labels);
        }

        public void Dispose()
        {
            _yoloCore.Dispose();
            GC.SuppressFinalize(this);
        }
    }
}
