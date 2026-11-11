using SkiaSharp;
using Snet.Yolo.Server.anomalib;
using Xunit;

namespace Snet.Yolo.Test;

/// <summary>热图的着色/尺寸约定与多线程缩放的结果一致性。</summary>
public sealed class AnomalibHeatmapAndResizeTests
{
    private static float[] Map(int width, int height, Func<int, int, float> value)
    {
        var map = new float[width * height];
        for (var y = 0; y < height; y++) { for (var x = 0; x < width; x++) { map[y * width + x] = value(x, y); } }
        return map;
    }

    [Theory]
    [InlineData(256, 256, 256, 256)]       // PaDiM 常用尺寸：不缩小
    [InlineData(512, 512, 512, 512)]
    [InlineData(640, 640, 320, 320)]
    [InlineData(1728, 1728, 432, 432)]     // 以前 600 毫秒 + 数 MB 的 data URL
    [InlineData(2048, 2048, 512, 512)]     // 以前 840 毫秒
    [InlineData(1000, 500, 500, 250)]      // 非方形：保持宽高比
    public void Rasterize_LimitsTheLongEdge(int width, int height, int expectedWidth, int expectedHeight)
    {
        var (pixels, outputWidth, outputHeight) = AnomalibHeatmapRenderer.Rasterize(new float[width * height], width, height, 0.5f);

        Assert.Equal((expectedWidth, expectedHeight), (outputWidth, outputHeight));
        Assert.Equal(outputWidth * outputHeight, pixels.Length);
        Assert.True(Math.Max(outputWidth, outputHeight) <= AnomalibHeatmapRenderer.MaximumEdge);
    }

    /// <summary>
    /// 着色是绝对的：一张正常图（分数都低于阈值）整幅透明；旧实现按本张图的最小/最大值拉伸，
    /// 正常图也会出现最红的“热点”，让人以为“热力图也显示异常”。
    /// </summary>
    [Fact]
    public void Rasterize_NormalImage_StaysTransparentInsteadOfStretchingToRed()
    {
        var normal = Map(64, 64, (x, y) => 0.10f + 0.15f * x / 63f);          // 最高 0.25，低于“阈值 − 0.25”的渐显起点

        var (pixels, _, _) = AnomalibHeatmapRenderer.Rasterize(normal, 64, 64, 0.5f);

        Assert.All(pixels, pixel => Assert.Equal(0, pixel.Alpha));
    }

    [Fact]
    public void Rasterize_AlphaGrowsWithTheScore_AndTheHotSpotIsTheMostOpaque()
    {
        var map = Map(64, 64, (x, y) => x == 40 && y == 20 ? 0.95f : x == 10 && y == 10 ? 0.52f : 0.1f);

        var (pixels, width, _) = AnomalibHeatmapRenderer.Rasterize(map, 64, 64, 0.5f);

        var hot = pixels[20 * width + 40];
        var borderline = pixels[10 * width + 10];
        var background = pixels[5 * width + 5];
        Assert.True(hot.Alpha > borderline.Alpha, $"{hot.Alpha} 应大于 {borderline.Alpha}");
        Assert.True(borderline.Alpha > background.Alpha, "刚过阈值的区域应比背景明显");
        Assert.Equal(0, background.Alpha);
        Assert.True(hot.Alpha >= 200);
    }

    /// <summary>缩小时块内取最大值：只占几个像素的小缺陷缩小后依然可见。</summary>
    [Fact]
    public void Rasterize_Downscale_KeepsATinyHotSpot()
    {
        var map = Map(2048, 2048, (x, y) => x == 1001 && y == 503 ? 1f : 0f);

        var (pixels, width, _) = AnomalibHeatmapRenderer.Rasterize(map, 2048, 2048, 0.5f);

        Assert.True(pixels[(503 / 4) * width + 1001 / 4].Alpha >= 200);
    }

    [Fact]
    public void Rasterize_FollowsTheChosenThreshold()
    {
        var map = Map(8, 8, (_, _) => 0.4f);

        var atModelThreshold = AnomalibHeatmapRenderer.Rasterize(map, 8, 8, 0.5f).Pixels[0].Alpha;
        var moreSensitive = AnomalibHeatmapRenderer.Rasterize(map, 8, 8, 0.35f).Pixels[0].Alpha;

        Assert.True(moreSensitive > atModelThreshold, "把阈值调低后，同样的分数应更醒目");
    }

    [Fact]
    public void Render_ProducesAPngDataUrl()
    {
        var url = AnomalibHeatmapRenderer.Render(Map(300, 200, (x, _) => x / 299f), 300, 200, 0.5f);

        const string prefix = "data:image/png;base64,";
        Assert.StartsWith(prefix, url);
        using var codec = SKCodec.Create(new MemoryStream(Convert.FromBase64String(url[prefix.Length..])))!;
        Assert.Equal((300, 200), (codec.Info.Width, codec.Info.Height));
    }

    /// <summary>
    /// 多线程缩放必须与单线程逐位相同：块边界处的行缓存重算不能改变任何一个输出元素。
    /// 覆盖 NCHW/NHWC、RGB/BGR 以及内置/外置归一化。
    /// </summary>
    [Theory]
    [InlineData(AnomalibTensorLayout.Nchw, AnomalibColorSpace.Rgb, true)]
    [InlineData(AnomalibTensorLayout.Nchw, AnomalibColorSpace.Bgr, false)]
    [InlineData(AnomalibTensorLayout.Nhwc, AnomalibColorSpace.Rgb, false)]
    [InlineData(AnomalibTensorLayout.Nhwc, AnomalibColorSpace.Bgr, true)]
    public void CreateInput_ParallelBlocks_AreBitIdenticalToSingleThread(AnomalibTensorLayout layout, AnomalibColorSpace colorSpace, bool embedded)
    {
        using var source = new SKBitmap(517, 389);
        var random = new Random(11);
        for (var y = 0; y < source.Height; y++)
        {
            for (var x = 0; x < source.Width; x++) { source.SetPixel(x, y, new SKColor((byte)random.Next(256), (byte)random.Next(256), (byte)random.Next(256))); }
        }
        var input = new AnomalibInputContract
        {
            Name = "image", Width = 96, Height = 80, Layout = layout, ColorSpace = colorSpace,
            Interpolation = AnomalibInterpolation.BilinearAntialias, ValueRange = AnomalibValueRange.ZeroToOne,
            NormalizationEmbedded = embedded,
            Mean = embedded ? null : [0.485f, 0.456f, 0.406f], Std = embedded ? null : [0.229f, 0.224f, 0.225f],
        };

        var single = AnomalibAntialiasResizer.CreateInput(source, input, 1);
        foreach (var blocks in new[] { 2, 3, 7, 16 })
        {
            var parallel = AnomalibAntialiasResizer.CreateInput(source, input, blocks);
            Assert.True(single.Buffer.Span.SequenceEqual(parallel.Buffer.Span), $"{blocks} 个并行块的结果与单线程不一致");
        }
    }

    /// <summary>
    /// 不透明 32 位像素走无边界检查、无 alpha 换算的快速路径（现场 BMP 解码后就是这种）；
    /// 结果必须与通用路径（预乘 alpha 位图）逐位相同。
    /// </summary>
    [Theory]
    [InlineData(SKColorType.Bgra8888)]
    [InlineData(SKColorType.Rgba8888)]
    public void CreateInput_OpaqueFastPath_IsBitIdenticalToTheGeneralPath(SKColorType colorType)
    {
        const int width = 301, height = 211;
        using var opaque = new SKBitmap(new SKImageInfo(width, height, colorType, SKAlphaType.Opaque));
        using var premultiplied = new SKBitmap(new SKImageInfo(width, height, colorType, SKAlphaType.Premul));
        var random = new Random(5);
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var color = new SKColor((byte)random.Next(256), (byte)random.Next(256), (byte)random.Next(256), 255);
                opaque.SetPixel(x, y, color);
                premultiplied.SetPixel(x, y, color);
            }
        }
        var input = new AnomalibInputContract
        {
            Name = "image", Width = 64, Height = 48, Layout = AnomalibTensorLayout.Nchw, ColorSpace = AnomalibColorSpace.Rgb,
            Interpolation = AnomalibInterpolation.BilinearAntialias, ValueRange = AnomalibValueRange.ZeroToOne, NormalizationEmbedded = true,
        };

        var fast = AnomalibAntialiasResizer.CreateInput(opaque, input, 1);
        var general = AnomalibAntialiasResizer.CreateInput(premultiplied, input, 1);

        Assert.True(fast.Buffer.Span.SequenceEqual(general.Buffer.Span));
    }

    [Fact]
    public void CreateInput_TinyOutputs_FallBackToOneBlock()
    {
        using var source = new SKBitmap(40, 40);
        source.Erase(new SKColor(10, 20, 30));
        var input = new AnomalibInputContract
        {
            Name = "image", Width = 4, Height = 4, Layout = AnomalibTensorLayout.Nchw, ColorSpace = AnomalibColorSpace.Rgb,
            Interpolation = AnomalibInterpolation.BilinearAntialias, ValueRange = AnomalibValueRange.ZeroToOne, NormalizationEmbedded = true,
        };

        var tensor = AnomalibAntialiasResizer.CreateInput(source, input, 64);

        Assert.Equal(10f / 255f, tensor[0, 0, 2, 2], 4);
        Assert.Equal(30f / 255f, tensor[0, 2, 3, 0], 4);
    }
}
