using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Text.Json.Nodes;
using Snet.Yolo.Tasks.Core.Config;
using Snet.Yolo.Tasks.Core.Models;
using Snet.Yolo.Tasks.Core.Serialization.Export;
using Snet.Yolo.Tasks.Core.Serialization.Import;
using Xunit;

namespace Snet.Yolo.Test;

/// <summary>覆盖现场五位小数标注、贴边框导出与重新导入，以及真正越界的诊断。</summary>
public sealed class YoloBoundaryRoundTripTests
{
    /// <summary>现场 labels/106.txt 的八行应保留，第四行五百万分之一的舍入越界贴齐左边缘。</summary>
    [Fact]
    public void Inspect_ImportsReportedEightRowsWithoutDroppingAnnotations()
    {
        const string labels = """
        7 0.18136 0.21494 0.21303 0.23796
        7 0.37025 0.76274 0.09083 0.06351
        7 0.69771 0.6094 0.03869 0.21721
        7 0.03027 0.34822 0.06055 0.10255
        7 0.63133 0.15702 0.11324 0.1677
        7 0.01253 0.23633 0.02279 0.06185
        7 0.18486 0.14396 0.06633 0.12533
        7 0.71007 0.25711 0.05298 0.04592
        """;
        var plan = Inspect(labels);
        var boxes = Assert.Single(plan.Images).Boxes;
        Assert.Equal(8, boxes.Count);
        Assert.Equal(1, plan.BoundaryAdjustedAnnotationCount);
        Assert.All(boxes, box => Assert.Equal(7, box.ClassIndex));
        Assert.Equal(0, boxes[3].XCenter - boxes[3].Width / 2, 12);
        Assert.Equal(0.060545, boxes[3].Width, 12);
        Assert.Equal(0.18136, boxes[0].XCenter);
    }

    /// <summary>四条边都兼容旧版五位小数舍入，并返回真正处于图内的框。</summary>
    [Theory]
    [InlineData("0 0.03027 0.5 0.06055 0.1")]
    [InlineData("0 0.96973 0.5 0.06055 0.1")]
    [InlineData("0 0.5 0.03027 0.1 0.06055")]
    [InlineData("0 0.5 0.96973 0.1 0.06055")]
    public void Inspect_NormalizesLegacyRoundingAtEveryBoundary(string line)
    {
        var box = Assert.Single(Assert.Single(Inspect(line).Images).Boxes);
        Assert.InRange(box.XCenter - box.Width / 2, 0, 1);
        Assert.InRange(box.XCenter + box.Width / 2, 0, 1);
        Assert.InRange(box.YCenter - box.Height / 2, 0, 1);
        Assert.InRange(box.YCenter + box.Height / 2, 0, 1);
    }

    /// <summary>内存 ZIP 与实际流式下载清单都保留足够精度，贴边框和微小正宽度可重新导入。</summary>
    [Theory]
    [InlineData(false, 6.054999)]
    [InlineData(true, 6.054999)]
    [InlineData(false, 0.0001)]
    [InlineData(true, 0.0001)]
    public async Task Export_BothZipPathsRoundTripBoundaryBoxes(bool streamed, double width)
    {
        var path = Path.Combine(Path.GetTempPath(), "yolo-boundary-" + Guid.NewGuid().ToString("N") + ".png");
        await File.WriteAllBytesAsync(path, [1], TestContext.Current.CancellationToken);
        try
        {
            var task = Task(width);
            var config = LabelingConfigParser.Parse("<View><Image name='image' value='$image'/><RectangleLabels name='box' toName='image'><Label value='part'/></RectangleLabels></View>");
            var export = streamed ? ExportService.YoloWithImageFiles([task], config, _ => path) : ExportService.YoloWithImages([task], config, _ => [1]);
            var text = Encoding.UTF8.GetString(Assert.Single(export.Files, file => file.Path.StartsWith("labels/", StringComparison.Ordinal)).Content);
            var numbers = text.Trim().Split(' ').Skip(1).Select(value => double.Parse(value, CultureInfo.InvariantCulture)).ToArray();
            Assert.Equal(width / 100, numbers[2], 9);
            Assert.True(numbers[2] > 0);
            using var output = new MemoryStream();
            await ZipHelper.WriteAsync(export.Files, output, TestContext.Current.CancellationToken);
            output.Position = 0;
            using var archive = new ZipArchive(output, ZipArchiveMode.Read);
            var box = Assert.Single(Assert.Single(YoloWithImagesImporter.Inspect(archive).Images).Boxes);
            Assert.True(box.Width > 0);
            Assert.InRange(box.XCenter - box.Width / 2, 0, 1);
        }
        finally { File.Delete(path); }
    }

    /// <summary>明显越界仍拒绝，并报告含空行的真实文本行号和具体边界值。</summary>
    [Fact]
    public void Inspect_RejectsRealOverflowWithActualLineNumber()
    {
        var error = Assert.Throws<InvalidDataException>(() => Inspect("\n0 0.5 0.5 0.1 0.1\n\n0 0.03 0.5 0.08 0.1\n"));
        Assert.Contains("labels/106.txt 第 4 行", error.Message);
        Assert.Contains("左", error.Message);
    }

    /// <summary>导出时也拒绝真实坏框，避免生成无法导回的 ZIP。</summary>
    [Fact]
    public void Export_RejectsRealOverflow()
    {
        var task = Task(5);
        task.Annotations[0].Result[0].Value!["x"] = -1;
        Assert.Throws<InvalidDataException>(() => YoloLabelExporter.Build(task, YoloTaskType.Detect, ["part"]));
    }

    /// <summary>舍入兼容不放宽非有限值、非法尺寸、非法类别或真正越界。</summary>
    [Theory]
    [InlineData("0 NaN 0.5 0.1 0.1")]
    [InlineData("0 0.5 Infinity 0.1 0.1")]
    [InlineData("0 0.5 0.5 0 0.1")]
    [InlineData("0 0.5 0.5 -0.1 0.1")]
    [InlineData("0 1.1 0.5 0.1 0.1")]
    [InlineData("8 0.5 0.5 0.1 0.1")]
    [InlineData("0 0.03026 0.5 0.06055 0.1")]
    public void Inspect_StillRejectsInvalidAnnotations(string line)
        => Assert.Throws<InvalidDataException>(() => Inspect(line));

    /// <summary>小包复现坐标问题，无需生成现场二十多 GB 图片。</summary>
    private static YoloImportPlan Inspect(string labels)
    {
        var classes = string.Join('\n', Enumerable.Range(0, 8).Select(index => $"{index} class{index}"));
        using var source = new MemoryStream(ZipHelper.Pack([new("classes.txt", Encoding.UTF8.GetBytes(classes)), new("images/106.png", [1]), new("labels/106.txt", Encoding.UTF8.GetBytes(labels))]));
        using var archive = new ZipArchive(source, ZipArchiveMode.Read);
        return YoloWithImagesImporter.Inspect(archive);
    }

    /// <summary>构造贴左边缘的有效原始框。</summary>
    private static AnnotationTask Task(double width) => new()
    {
        Id = 106, Data = new() { ["image"] = "original.png" }, Annotations = [new() { Result = [new() { Type = RegionType.RectangleLabels,
            Value = new JsonObject { ["x"] = 0d, ["y"] = 20d, ["width"] = width, ["height"] = 10d, ["rectanglelabels"] = new JsonArray("part") } }] }],
    };
}
