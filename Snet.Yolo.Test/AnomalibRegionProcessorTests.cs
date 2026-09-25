using Snet.Yolo.Server.Anomalib;
using Xunit;

namespace Snet.Yolo.Test;

/// <summary>
/// Anomalib 异常掩码区域提取与坐标回映测试。
/// </summary>
public sealed class AnomalibRegionProcessorTests
{
    /// <summary>大量互不相邻的异常碎片应保持独立，不因后处理发生病态全量重扫。</summary>
    [Fact]
    public void Extract_ManySeparateRegions_RemainsStable()
    {
        const int width = 256;
        const int height = 256;
        var mask = new byte[width * height];
        for (var y = 0; y < height; y += 4)
        {
            for (var x = 0; x < width; x += 4)
            {
                mask[y * width + x] = 1;
                mask[y * width + x + 1] = 1;
                mask[(y + 1) * width + x] = 1;
                mask[(y + 1) * width + x + 1] = 1;
            }
        }

        var regions = AnomalibRegionProcessor.Extract(mask, width, height, new AnomalibRegionOptions { MinimumArea = 4 });

        Assert.Equal(4096, regions.Count);
    }

    /// <summary>
    /// 验证八邻域连通、最小面积过滤和确定性排序。
    /// </summary>
    [Fact]
    public void Extract_FiltersNoiseAndUsesEightConnectivity()
    {
        var mask = Mask(
            "110000",
            "011000",
            "000000",
            "000010",
            "000001");

        var regions = AnomalibRegionProcessor.Extract(mask, 6, 5, new AnomalibRegionOptions { MinimumArea = 3 });

        var region = Assert.Single(regions);
        Assert.Equal(new PixelRectangle(0, 0, 3, 2), region.Bounds);
        Assert.Equal(4, region.PixelArea);
    }

    /// <summary>
    /// 验证相邻区域按指定像素距离合并，并保留像素面积总和。
    /// </summary>
    [Fact]
    public void Extract_MergesNearbyRegions()
    {
        var mask = Mask(
            "110011",
            "110011");

        var regions = AnomalibRegionProcessor.Extract(mask, 6, 2, new AnomalibRegionOptions
        {
            MinimumArea = 1,
            MergeDistance = 2
        });

        var region = Assert.Single(regions);
        Assert.Equal(new PixelRectangle(0, 0, 6, 2), region.Bounds);
        Assert.Equal(8, region.PixelArea);
    }

    /// <summary>
    /// 验证区域合并关系具有传递性，不受连通域扫描顺序影响。
    /// </summary>
    [Fact]
    public void Extract_MergesTransitivelyRegardlessOfScanOrder()
    {
        var mask = Mask(
            "1000000000",
            "0000001000",
            "0001000000");

        var regions = AnomalibRegionProcessor.Extract(mask, 10, 3, new AnomalibRegionOptions
        {
            MinimumArea = 1,
            MergeDistance = 3
        });

        var region = Assert.Single(regions);
        Assert.Equal(new PixelRectangle(0, 0, 7, 3), region.Bounds);
        Assert.Equal(3, region.PixelArea);
    }

    /// <summary>
    /// 验证扩框不会超过掩码边界。
    /// </summary>
    [Fact]
    public void Extract_ExpandsAndClampsBounds()
    {
        var mask = Mask(
            "1000",
            "0000",
            "0000",
            "0000");

        var regions = AnomalibRegionProcessor.Extract(mask, 4, 4, new AnomalibRegionOptions
        {
            MinimumArea = 1,
            PaddingRatio = 1
        });

        Assert.Equal(new PixelRectangle(0, 0, 2, 2), Assert.Single(regions).Bounds);
    }

    /// <summary>
    /// 验证拉伸预处理下从异常图坐标精确映射回原图坐标。
    /// </summary>
    [Fact]
    public void MapToOriginal_Stretch_UsesOriginalDimensions()
    {
        var mapped = AnomalibCoordinateMapper.MapToOriginal(
            new PixelRectangle(25, 10, 50, 20),
            mapWidth: 100,
            mapHeight: 50,
            inputWidth: 200,
            inputHeight: 100,
            originalWidth: 1000,
            originalHeight: 500,
            AnomalibResizeMode.Stretch);

        Assert.Equal(new PixelRectangle(250, 100, 500, 200), mapped);
    }

    /// <summary>
    /// 验证 letterbox 产生的上下填充会在坐标回映时被移除。
    /// </summary>
    [Fact]
    public void MapToOriginal_Letterbox_RemovesPadding()
    {
        var mapped = AnomalibCoordinateMapper.MapToOriginal(
            new PixelRectangle(0, 25, 100, 50),
            mapWidth: 100,
            mapHeight: 100,
            inputWidth: 200,
            inputHeight: 200,
            originalWidth: 400,
            originalHeight: 200,
            AnomalibResizeMode.Letterbox);

        Assert.Equal(new PixelRectangle(0, 0, 400, 200), mapped);
    }

    /// <summary>
    /// 验证 center crop 的裁剪偏移会在坐标回映时恢复。
    /// </summary>
    [Fact]
    public void MapToOriginal_CenterCrop_RestoresCropOffset()
    {
        var mapped = AnomalibCoordinateMapper.MapToOriginal(
            new PixelRectangle(0, 0, 100, 100),
            mapWidth: 100,
            mapHeight: 100,
            inputWidth: 200,
            inputHeight: 200,
            originalWidth: 400,
            originalHeight: 200,
            AnomalibResizeMode.CenterCrop);

        Assert.Equal(new PixelRectangle(100, 0, 200, 200), mapped);
    }

    /// <summary>
    /// 验证图像结果 DTO 保留原图尺寸、异常图尺寸和区域信息。
    /// </summary>
    [Fact]
    public void ImageResult_PreservesDimensionsAndRegions()
    {
        var result = new AnomalibImageResult
        {
            ImageScore = 0.82f,
            IsAnomalous = true,
            OriginalWidth = 1920,
            OriginalHeight = 1080,
            MapWidth = 256,
            MapHeight = 256,
            InferenceMilliseconds = 12,
            Regions =
            [
                new AnomalibRegionResult
                {
                    RegionId = 1,
                    Bounds = new PixelRectangle(10, 20, 30, 40),
                    PixelArea = 500,
                    MaximumScore = 0.91f,
                    MeanScore = 0.77f
                }
            ]
        };

        Assert.Equal(1920, result.OriginalWidth);
        Assert.Equal(256, result.MapWidth);
        Assert.Equal(0.91f, Assert.Single(result.Regions).MaximumScore);
    }

    /// <summary>
    /// 把由 0 和 1 组成的文本行转换为紧凑的二值掩码。
    /// </summary>
    private static byte[] Mask(params string[] rows)
        => rows.SelectMany(static row => row.Select(static value => value == '1' ? (byte)1 : (byte)0)).ToArray();
}
