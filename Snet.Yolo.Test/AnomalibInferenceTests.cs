using SkiaSharp;
using Snet.Yolo.Server.Anomalib;
using Snet.Yolo.Tasks.Services;
using Xunit;

namespace Snet.Yolo.Test;

/// <summary>Anomalib ONNX 输入图像预处理测试。</summary>
public sealed class AnomalibInferenceTests
{
    /// <summary>BGR 张量应使用对应 RGB 原色的均值和标准差，避免通道交换后归一化错位。</summary>
    [Fact]
    public void CreateInput_Bgr_UsesSourceColorNormalization()
    {
        using var source = new SKBitmap(1, 1);
        source.SetPixel(0, 0, new SKColor(128, 64, 32));
        var input = new AnomalibInputContract
        {
            Width = 1,
            Height = 1,
            Layout = AnomalibTensorLayout.Nchw,
            ColorSpace = AnomalibColorSpace.Bgr,
            ResizeMode = AnomalibResizeMode.Stretch,
            ValueRange = AnomalibValueRange.ZeroToOne,
            NormalizationEmbedded = false,
            Mean = [0.1f, 0.2f, 0.3f],
            Std = [0.5f, 0.25f, 0.1f],
        };

        var tensor = AnomalibInferenceService.CreateInput(source, input);

        Assert.InRange(tensor[0, 0, 0, 0], (32f / 255 - 0.3f) / 0.1f - 0.001f, (32f / 255 - 0.3f) / 0.1f + 0.001f);
        Assert.InRange(tensor[0, 1, 0, 0], (64f / 255 - 0.2f) / 0.25f - 0.001f, (64f / 255 - 0.2f) / 0.25f + 0.001f);
        Assert.InRange(tensor[0, 2, 0, 0], (128f / 255 - 0.1f) / 0.5f - 0.001f, (128f / 255 - 0.1f) / 0.5f + 0.001f);
    }
}
