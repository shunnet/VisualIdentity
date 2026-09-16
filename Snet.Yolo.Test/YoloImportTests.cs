namespace Snet.Yolo.Test;

using Snet.Yolo.Tasks.Core.Config;
using Snet.Yolo.Tasks.Core.Editing;
using Snet.Yolo.Tasks.Core.Models;
using Snet.Yolo.Tasks.Core.Serialization.Export;
using Snet.Yolo.Tasks.Core.Serialization.Import;
using System.IO.Compression;
using System.Text;
using System.Text.Json.Nodes;
using Xunit;

/// <summary>验证“YOLO (with images)”导入自检与颜色分配。</summary>
public sealed class YoloImportTests
{
    /// <summary>应用导出的 ZIP 应能完整恢复类别、图片和矩形标注。</summary>
    [Fact]
    public void Inspect_RoundTripsYoloWithImagesExport()
    {
        const string configXml = "<View><Image name=\"image\" value=\"$image\"/><RectangleLabels name=\"rect\" toName=\"image\"><Label value=\"part\" background=\"#E6194B\"/></RectangleLabels></View>";
        var value = new JsonObject { ["x"] = 10d, ["y"] = 20d, ["width"] = 30d, ["height"] = 40d, ["rectanglelabels"] = new JsonArray("part") };
        var task = new AnnotationTask
        {
            Data = new JsonObject { ["image"] = "/uploads/part.jpg" },
            Annotations = new List<Annotation> { new() { Result = new List<ResultRow> { new() { Type = RegionType.RectangleLabels, Value = value } } } },
        };
        var export = ExportService.YoloWithImages(new[] { task }, LabelingConfigParser.Parse(configXml), _ => new byte[] { 1, 2, 3 });
        using var stream = new MemoryStream(ZipHelper.Pack(export.Files));
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read);

        var plan = YoloWithImagesImporter.Inspect(archive);

        Assert.Equal(new[] { "part" }, plan.Classes);
        var image = Assert.Single(plan.Images);
        Assert.Equal("images/1.jpg", image.ImageEntryPath);
        var box = Assert.Single(image.Boxes);
        Assert.Equal(0, box.ClassIndex);
        Assert.Equal(0.25d, box.XCenter, 8);
        Assert.Equal(0.40d, box.YCenter, 8);
        Assert.Equal(0.30d, box.Width, 8);
        Assert.Equal(0.40d, box.Height, 8);
    }

    /// <summary>有图片但缺少对应标注文件时必须在写入项目前失败。</summary>
    [Fact]
    public void Inspect_RejectsImageWithoutMatchingLabel()
    {
        var files = new[]
        {
            new ExportFile("classes.txt", Encoding.UTF8.GetBytes("0 part\n")),
            new ExportFile("images/1.jpg", new byte[] { 1, 2, 3 }),
        };
        using var stream = new MemoryStream(ZipHelper.Pack(files));
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read);

        var exception = Assert.Throws<InvalidDataException>(() => YoloWithImagesImporter.Inspect(archive));

        Assert.Contains("缺少对应标注", exception.Message, StringComparison.Ordinal);
    }

    /// <summary>ZIP 条目不得通过父目录段逃出预期数据集结构。</summary>
    [Fact]
    public void Inspect_RejectsDirectoryTraversalEntry()
    {
        var files = new[]
        {
            new ExportFile("classes.txt", Encoding.UTF8.GetBytes("0 part\n")),
            new ExportFile("../images/1.jpg", new byte[] { 1, 2, 3 }),
            new ExportFile("../labels/1.txt", Encoding.UTF8.GetBytes("0 0.5 0.5 0.2 0.2\n")),
        };
        using var stream = new MemoryStream(ZipHelper.Pack(files));
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read);

        Assert.Throws<InvalidDataException>(() => YoloWithImagesImporter.Inspect(archive));
    }

    /// <summary>扩展色板应为大量标签生成互不重复的颜色。</summary>
    [Fact]
    public void ColorForIndex_GeneratesDistinctColors()
    {
        var colors = Enumerable.Range(0, 1_000).Select(LabelPalette.ColorForIndex).ToArray();

        Assert.Equal(colors.Length, colors.Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.All(colors, color => Assert.Matches("^#[0-9A-Fa-f]{6}$", color));
    }

    /// <summary>导入类别应合并到项目配置，并修复已有的重复颜色。</summary>
    [Fact]
    public void MergeLabels_AddsImportedClassesWithUniqueColors()
    {
        const string configXml = "<View><Image name=\"image\" value=\"$image\"/><RectangleLabels name=\"rect\" toName=\"image\"><Label value=\"Existing\" background=\"#E6194B\"/><Label value=\"DuplicateColor\" background=\"#E6194B\"/></RectangleLabels></View>";

        var merged = YoloWithImagesImporter.MergeLabels(configXml, new[] { "existing", "Imported" });
        var config = LabelingConfigParser.Parse(merged.Xml);
        var labels = Assert.Single(config.Controls).Labels;

        Assert.Equal(new[] { "Existing", "Imported" }, merged.ClassNames);
        Assert.Contains(labels, label => label.Value == "Imported");
        Assert.Equal(labels.Count, labels.Select(label => label.Background).Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }
}
