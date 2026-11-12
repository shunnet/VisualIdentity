using SkiaSharp;
using Snet.Yolo.Server.yolo.Extensions;
using Snet.Yolo.Server.yolo.Handlers;
using Snet.Yolo.Server.yolo.Models;
using Xunit;

namespace Snet.Yolo.Test;

/// <summary>
/// 推理前把原图缩到模型输入尺寸的采样方式必须与 Ultralytics 的训练/预测一致：cv2.INTER_LINEAR（双线性、不做 mipmap、不抗锯齿）。
/// 以前默认是最近邻：同一个模型在 3024×3024 照片上的 mAP50 从 0.900 掉到 0.849，召回率从 0.797 掉到 0.743。
/// </summary>
public sealed class YoloInputResizeTests
{
    private const int SourceSize = 256;
    private const int TargetSize = 64;
    private const int Scale = SourceSize / TargetSize;

    [Fact]
    public void DefaultSamplingOptions_AreBilinearWithoutMipmaps()
    {
        var options = new YoloOptions().SamplingOptions;

        Assert.Equal(SKFilterMode.Linear, options.Filter);
        Assert.Equal(SKMipmapMode.None, options.Mipmap);
        Assert.False(options.UseCubic);
    }

    [Fact]
    public void DownscaleWithDefaultOptions_IsThePlainBilinearAverageOfTheFourNearestSourcePixels()
    {
        // 缩小 4 倍时，目标像素中心正好落在源像素 (4x+1, 4x+2) 的中间，OpenCV 的 INTER_LINEAR 就是这 2×2 像素的平均。
        // 图案里相邻像素差 37 / 91 个灰度级：最近邻只会取其中一个，带 mipmap 的缩小会平均 4×4，两者都会偏离平均值很多。
        using var source = new SKBitmap(SourceSize, SourceSize, SKColorType.Rgba8888, SKAlphaType.Opaque);
        for (var y = 0; y < SourceSize; y++)
        {
            for (var x = 0; x < SourceSize; x++) { source.SetPixel(x, y, new SKColor(Pattern(x, y, 0), Pattern(x, y, 1), Pattern(x, y, 2))); }
        }
        using var image = SKImage.FromBitmap(source);
        using var buffer = new PinnedMemoryBuffer(new SKImageInfo(TargetSize, TargetSize, SKColorType.Rgb888x, SKAlphaType.Opaque));

        image.ResizeImageProportional(new YoloOptions().SamplingOptions, buffer);

        for (var y = 0; y < TargetSize; y++)
        {
            for (var x = 0; x < TargetSize; x++)
            {
                for (var channel = 0; channel < 3; channel++)
                {
                    var actual = buffer.Buffer[(y * TargetSize + x) * 4 + channel];
                    var expected = BilinearAverage(x, y, channel);
                    Assert.True(Math.Abs(actual - expected) <= 2, $"像素 ({x},{y}) 通道 {channel}：实际 {actual}，双线性应为 {expected:0.#}。");
                }
            }
        }
    }

    private static byte Pattern(int x, int y, int channel) => (byte)((x * 37 + y * 91 + channel * 53) % 256);

    private static double BilinearAverage(int x, int y, int channel)
    {
        var sourceX = x * Scale + Scale / 2 - 1;
        var sourceY = y * Scale + Scale / 2 - 1;
        return (Pattern(sourceX, sourceY, channel) + Pattern(sourceX + 1, sourceY, channel)
                + Pattern(sourceX, sourceY + 1, channel) + Pattern(sourceX + 1, sourceY + 1, channel)) / 4d;
    }
}
