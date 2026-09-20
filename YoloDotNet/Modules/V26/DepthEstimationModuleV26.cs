// SPDX-License-Identifier: MIT
// SPDX-FileCopyrightText: 2026 Niklas Swärd
// https://github.com/NickSwardh/YoloDotNet

namespace YoloDotNet.Modules.V26
{
    /// <summary>Processes YOLO26 monocular depth-estimation outputs.</summary>
    internal sealed class DepthEstimationModuleV26 : IDepthEstimationModule
    {
        private readonly YoloCore _yoloCore;
        private readonly int _modelWidth;
        private readonly int _modelHeight;
        private readonly int _outputWidth;
        private readonly int _outputHeight;

        public DepthEstimationModuleV26(YoloCore yoloCore)
        {
            _yoloCore = yoloCore;

            var inputShape = yoloCore.OnnxModel.InputShapes.Single().Value;
            var outputShape = yoloCore.OnnxModel.OutputShapes.Single().Value;
            if (outputShape.Length != 4 || outputShape[0] != 1 || outputShape[1] != 1 || outputShape[2] <= 0 || outputShape[3] <= 0)
                throw new YoloDotNetModelException("YOLO26 depth output must have shape [1, 1, height, width].");

            _modelHeight = checked((int)inputShape[2]);
            _modelWidth = checked((int)inputShape[3]);
            _outputHeight = outputShape[2];
            _outputWidth = outputShape[3];
        }

        public OnnxModel OnnxModel => _yoloCore.OnnxModel;

        public DepthEstimation ProcessImage<T>(T image, SKRectI? roi = null)
        {
            using var inferenceResult = _yoloCore.Run(image, roi);
            var source = inferenceResult.OrtSpan0;
            if (source.IsEmpty)
                throw new YoloDotNetModelException("YOLO26 depth output must use Float32 or Float16 tensor values.");

            var imageSize = inferenceResult.ImageOriginalSize;
            var depth = DenseOutputProjector.ProjectBilinear(
                source,
                _outputWidth,
                _outputHeight,
                imageSize.Width,
                imageSize.Height,
                _modelWidth,
                _modelHeight,
                _yoloCore.YoloOptions.ImageResize,
                _yoloCore.CalculateGain(imageSize));

            var minimum = float.PositiveInfinity;
            var maximum = float.NegativeInfinity;
            foreach (var value in depth)
            {
                if (!float.IsFinite(value))
                    throw new YoloDotNetModelException("YOLO26 depth output contains a non-finite value.");

                minimum = MathF.Min(minimum, value);
                maximum = MathF.Max(maximum, value);
            }

            return new DepthEstimation(imageSize.Width, imageSize.Height, depth, minimum, maximum);
        }

        public void Dispose()
        {
            _yoloCore.Dispose();
            GC.SuppressFinalize(this);
        }
    }
}
