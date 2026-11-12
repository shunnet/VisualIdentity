using Snet.Yolo.Tasks.Core.Config;
using Snet.Yolo.Tasks.Core.Models;
using Snet.Yolo.Tasks.Core.Training;
using System.Text.Json.Nodes;
using Xunit;

namespace Snet.Yolo.Test;

/// <summary>训练前的数据画像：框相对原图的尺寸、图片分辨率与可用标注数。</summary>
public sealed class YoloDatasetProfileTests
{
    private static AnnotationTask Task(params ResultRow[] rows)
        => new() { Data = new JsonObject { ["image"] = "a.jpg" }, Annotations = [new() { Result = [.. rows] }] };

    private static ResultRow Rectangle(double width, double height, double originalWidth, double originalHeight)
        => new()
        {
            Type = RegionType.RectangleLabels, OriginalWidth = originalWidth, OriginalHeight = originalHeight,
            Value = new JsonObject { ["x"] = 10, ["y"] = 10, ["width"] = width, ["height"] = height, ["rectanglelabels"] = new JsonArray("box") },
        };

    [Fact]
    public void From_ConvertsPercentBoxesToSourcePixels_AndKeepsTheImageLongEdge()
    {
        // 3024×3024 的照片里一个占 10% × 20% 的框：短边 = 3024 × 10% = 302.4 px
        var profile = YoloDatasetProfile.From([Task(Rectangle(10, 20, 3024, 3024))], YoloTaskType.Detect, classCount: 1);

        var box = Assert.Single(profile.Boxes);
        Assert.Equal(302.4, box.ShortSidePixels, 3);
        Assert.Equal(3024, box.LongEdgePixels);
        Assert.Equal(3024, profile.MedianLongEdge);
        Assert.Equal((1, 1, 1), (profile.ImageCount, profile.AnnotatedImageCount, profile.BoxCount));
    }

    [Fact]
    public void PixelsAt_UsesTheLetterboxScaleOfTheLongEdge()
    {
        // 640×480 的图、占 50% 宽 20% 高的框：短边 = 480 × 20% = 96 px（原图）；imgsz=320 时图片按长边缩 0.5 → 48 px
        var profile = YoloDatasetProfile.From([Task(Rectangle(50, 20, 640, 480))], YoloTaskType.Detect, 1);

        Assert.Equal(96, profile.Boxes[0].ShortSidePixels, 3);
        Assert.Equal(48, profile.Boxes[0].PixelsAt(320), 3);
        Assert.Equal(96, profile.Boxes[0].PixelsAt(640), 3);
    }

    [Fact]
    public void From_SkipsImagesWithoutAnnotations_AndCancelledAnnotations()
    {
        var plain = new AnnotationTask { Data = new JsonObject { ["image"] = "b.jpg" } };
        var cancelled = new AnnotationTask
        {
            Data = new JsonObject { ["image"] = "c.jpg" },
            Annotations = [new() { WasCancelled = true, Result = [Rectangle(10, 10, 1000, 1000)] }],
        };
        var noImage = new AnnotationTask { Annotations = [new() { Result = [Rectangle(10, 10, 1000, 1000)] }] };

        var profile = YoloDatasetProfile.From([plain, cancelled, noImage, Task(Rectangle(10, 10, 1000, 1000))], YoloTaskType.Detect, 1);

        Assert.Equal(3, profile.ImageCount);              // 没有 image 的任务不算图片
        Assert.Equal(1, profile.AnnotatedImageCount);     // 无标注与已取消标注的不算
        Assert.Equal(1, profile.BoxCount);
    }

    [Fact]
    public void From_UsesTheBoundingBoxOfPolygonsForSegmentation()
    {
        var polygon = new ResultRow
        {
            Type = RegionType.PolygonLabels, OriginalWidth = 1000, OriginalHeight = 500,
            Value = new JsonObject { ["points"] = new JsonArray(new JsonArray(10, 20), new JsonArray(30, 20), new JsonArray(30, 60), new JsonArray(10, 60)) },
        };

        var profile = YoloDatasetProfile.From([Task(polygon)], YoloTaskType.Segment, 1);

        // 外接框 20% 宽 × 40% 高 → 200 px × 200 px，短边 200
        Assert.Equal(200, Assert.Single(profile.Boxes).ShortSidePixels, 3);
    }

    [Fact]
    public void From_CountsBrushImagesForSegmentation_ButTheyHaveNoBoxSize()
    {
        var brush = new ResultRow
        {
            Type = RegionType.BrushLabels, OriginalWidth = 1000, OriginalHeight = 500,
            Value = new JsonObject { ["format"] = "rle", ["rle"] = new JsonArray(1, 2, 3), ["brushlabels"] = new JsonArray("box") },
        };

        var profile = YoloDatasetProfile.From([Task(brush)], YoloTaskType.Segment, 1);

        Assert.Equal((1, 1, 0), (profile.ImageCount, profile.AnnotatedImageCount, profile.BoxCount));
    }

    [Fact]
    public void From_ClassificationCountsChosenImagesOnly_AndHasNoBoxes()
    {
        var chosen = Task(new ResultRow { Type = RegionType.Choices, Value = new JsonObject { ["choices"] = new JsonArray("cat") } });
        var unlabeled = new AnnotationTask { Data = new JsonObject { ["image"] = "d.jpg" } };

        var profile = YoloDatasetProfile.From([chosen, unlabeled], YoloTaskType.Classify, 2);

        Assert.Equal((2, 1, 0), (profile.ImageCount, profile.AnnotatedImageCount, profile.BoxCount));
        Assert.Empty(profile.Boxes);
    }

    [Fact]
    public void BoxPixelsQuantile_ReturnsTheRequestedPercentile_AndZeroWithoutBoxes()
    {
        var boxes = Enumerable.Range(1, 101).Select(index => new YoloBoxSample(index, 640)).ToArray();
        var profile = new YoloDatasetProfile(101, 101, 101, 1, boxes, 640);

        Assert.Equal(6d / 640, profile.ShortSideFractionQuantile(0.05), 9);   // 与输入尺寸无关的比例
        Assert.Equal(6, profile.BoxPixelsQuantile(640, 0.05), 6);      // 1..101 的 5% 分位 = 6
        Assert.Equal(51, profile.BoxPixelsQuantile(640, 0.5), 6);
        Assert.Equal(3, profile.BoxPixelsQuantile(320, 0.05), 6);      // 输入减半，像素减半
        Assert.Equal(0, new YoloDatasetProfile(0, 0, 0, 0, [], 0).BoxPixelsQuantile(640, 0.05));
    }
}
