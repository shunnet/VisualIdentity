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

/// <summary>验证带图片的 YOLO 数据集导入自检与颜色分配。</summary>
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

    /// <summary>
    /// 缺少标注文件的图片按“这张图没有目标”导入，而不是让整个包失败。
    /// YOLO 生态（以及 Roboflow 的导出）都用“没有 .txt”表示无标注图片，严格报错会让正常数据集永远导不进来。
    /// </summary>
    [Fact]
    public void Inspect_TreatsMissingLabelFileAsImageWithoutAnnotations()
    {
        var files = new[]
        {
            new ExportFile("classes.txt", Encoding.UTF8.GetBytes("0 part\n")),
            new ExportFile("images/1.jpg", new byte[] { 1, 2, 3 }),
        };
        using var stream = new MemoryStream(ZipHelper.Pack(files));
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read);

        var plan = YoloWithImagesImporter.Inspect(archive);

        var image = Assert.Single(plan.Images);
        Assert.Empty(image.Boxes);
        Assert.Equal(1, plan.UnlabeledImageCount);
        Assert.Equal(0, plan.AnnotationCount);
    }

    /// <summary>
    /// 放宽“图片可以没有标注文件”之后必须保留的防呆：
    /// 标注被放在 labels 之外的目录时，不能变成“静默导入 0 个标注”。
    /// </summary>
    [Fact]
    public void Inspect_RejectsAnnotationsOutsideLabelsDirectory()
    {
        var files = new[]
        {
            new ExportFile("classes.txt", Encoding.UTF8.GetBytes("0 part\n")),
            new ExportFile("images/1.jpg", new byte[] { 1, 2, 3 }),
            new ExportFile("annotations/1.txt", Encoding.UTF8.GetBytes("0 0.5 0.5 0.2 0.2\n")),
        };
        using var stream = new MemoryStream(ZipHelper.Pack(files));
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read);

        var exception = Assert.Throws<InvalidDataException>(() => YoloWithImagesImporter.Inspect(archive));

        Assert.Contains("labels 目录", exception.Message, StringComparison.Ordinal);
        Assert.Contains("annotations/1.txt", exception.Message, StringComparison.Ordinal);
    }

    /// <summary>整包确实没有标注（纯背景图数据集）时应当正常导入，只带上未标注计数。</summary>
    [Fact]
    public void Inspect_AcceptsGenuinelyUnannotatedDataset()
    {
        var files = new[]
        {
            new ExportFile("data.yaml", Encoding.UTF8.GetBytes("nc: 1\nnames: ['part']\n")),
            new ExportFile("README.roboflow.txt", Encoding.UTF8.GetBytes("exported via roboflow.com\n")),
            new ExportFile("train/images/a.jpg", new byte[] { 1, 2, 3 }),
            new ExportFile("train/images/b.jpg", new byte[] { 1, 2, 3, 4 }),
        };
        using var stream = new MemoryStream(ZipHelper.Pack(files));
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read);

        var plan = YoloWithImagesImporter.Inspect(archive);

        Assert.Equal(2, plan.Images.Count);
        Assert.Equal(2, plan.UnlabeledImageCount);
        Assert.Equal(0, plan.AnnotationCount);
    }

    /// <summary>有标注却没有图片说明包被破坏，必须在写入项目前失败。</summary>
    [Fact]
    public void Inspect_RejectsLabelWithoutMatchingImage()
    {
        var files = new[]
        {
            new ExportFile("classes.txt", Encoding.UTF8.GetBytes("0 part\n")),
            new ExportFile("images/1.jpg", new byte[] { 1, 2, 3 }),
            new ExportFile("labels/2.txt", Encoding.UTF8.GetBytes("0 0.5 0.5 0.2 0.2\n")),
        };
        using var stream = new MemoryStream(ZipHelper.Pack(files));
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read);

        var exception = Assert.Throws<InvalidDataException>(() => YoloWithImagesImporter.Inspect(archive));

        Assert.Contains("没有对应图片", exception.Message, StringComparison.Ordinal);
    }

    /// <summary>图片格式不受支持时要给出明确的“格式”提示，而不是含糊的“标注没有对应图片”。</summary>
    [Fact]
    public void Inspect_ReportsUnsupportedImageFormatByItsOwnName()
    {
        var files = new[]
        {
            new ExportFile("classes.txt", Encoding.UTF8.GetBytes("0 part\n")),
            new ExportFile("images/1.jpg", new byte[] { 1, 2, 3 }),
            new ExportFile("images/2.tif", new byte[] { 1, 2, 3 }),
            new ExportFile("labels/2.txt", Encoding.UTF8.GetBytes("0 0.5 0.5 0.2 0.2\n")),
        };
        using var stream = new MemoryStream(ZipHelper.Pack(files));
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read);

        var exception = Assert.Throws<InvalidDataException>(() => YoloWithImagesImporter.Inspect(archive));

        Assert.Contains("不受支持的图片格式", exception.Message, StringComparison.Ordinal);
        Assert.Contains("images/2.tif", exception.Message, StringComparison.Ordinal);
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

    /// <summary>
    /// Roboflow（YOLO26 / YOLOv8 / YOLOv5 导出）包的形态：没有 classes.txt，
    /// 类别写在 data.yaml，图片与标注按 train/valid/test 分目录，且同一份 data.yaml 会被重复写进压缩包。
    /// 「D:\Dowload Edge\feathers detection.v1i.yolo26.zip」就是这种包。
    /// </summary>
    [Fact]
    public void Inspect_ImportsRoboflowStylePackage()
    {
        const string data = "train: ../train/images\nval: ../valid/images\ntest: ../test/images\n\nnc: 2\nnames: ['Feathers', \"Down\"]\n\nroboflow:\n  workspace: project-oqzd9\n  project: feathers-detection-4ehdp\n";
        var files = new[]
        {
            // Roboflow 会把同一份 data.yaml 写三遍（每份内容完全一致）。
            new ExportFile("data.yaml", Encoding.UTF8.GetBytes(data)),
            new ExportFile("data.yaml", Encoding.UTF8.GetBytes(data)),
            new ExportFile("data.yaml", Encoding.UTF8.GetBytes(data)),
            new ExportFile("README.roboflow.txt", Encoding.UTF8.GetBytes("This dataset was exported via roboflow.com\n")),
            new ExportFile("README.dataset.txt", Encoding.UTF8.GetBytes("# feathers detection\n")),
            new ExportFile("train/images/a.jpg", new byte[] { 1, 2, 3 }),
            new ExportFile("train/labels/a.txt", Encoding.UTF8.GetBytes("0 0.5444322545255074 0.46483352038907594 0.1267142073505211 0.10362888140665918\n1 0.25 0.25 0.1 0.1\n")),
            new ExportFile("valid/images/b.jpg", new byte[] { 1, 2, 3, 4 }),
            new ExportFile("valid/labels/b.txt", Encoding.UTF8.GetBytes("1 0.5 0.5 0.4 0.4\n")),
            new ExportFile("test/images/c.jpg", new byte[] { 1, 2, 3, 4, 5 }),
            // 无标注图片：Roboflow 不会为它生成空标注文件。
        };
        using var stream = new MemoryStream(ZipHelper.Pack(files));
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read);

        var plan = YoloWithImagesImporter.Inspect(archive);

        Assert.Equal(new[] { "Feathers", "Down" }, plan.Classes);
        Assert.Equal(3, plan.Images.Count);
        Assert.Equal(3, plan.AnnotationCount);
        Assert.Equal(1, plan.UnlabeledImageCount);

        var trainImage = Assert.Single(plan.Images, image => image.ImageEntryPath == "train/images/a.jpg");
        Assert.Equal("a.jpg", trainImage.OriginalFileName);
        Assert.Equal(new[] { 0, 1 }, trainImage.Boxes.Select(box => box.ClassIndex));
        Assert.Equal(0.5444322545255074d, trainImage.Boxes[0].XCenter, 12);

        var validImage = Assert.Single(plan.Images, image => image.ImageEntryPath == "valid/images/b.jpg");
        Assert.Equal(1, Assert.Single(validImage.Boxes).ClassIndex);

        Assert.Empty(Assert.Single(plan.Images, image => image.ImageEntryPath == "test/images/c.jpg").Boxes);
    }

    /// <summary>data.yaml 的 names 支持 Roboflow / Ultralytics / YOLOv5 的常见写法。</summary>
    [Theory]
    [InlineData("nc: 2\nnames: ['a', 'b']\n")]
    [InlineData("nc: 2\nnames: [\"a\", \"b\"]\n")]
    [InlineData("nc: 2\nnames: [a, b]\n")]
    [InlineData("names:\n- a\n- b\n")]
    [InlineData("names:\n  - a\n  - b\n")]
    [InlineData("names: {0: a, 1: b}\n")]
    [InlineData("names: {0: 'a', 1: \"b\"}\n")]
    [InlineData("# 数据集说明\nnc: 2  # 两个类别\nnames: ['a', 'b']  # 类别名\nroboflow:\n  workspace: w\n")]
    public void Inspect_ReadsClassNamesFromDataYaml(string yaml)
    {
        var plan = InspectDataYamlPackage(yaml);

        Assert.Equal(new[] { "a", "b" }, plan.Classes);
        Assert.Equal(0, Assert.Single(Assert.Single(plan.Images).Boxes).ClassIndex);
    }

    /// <summary>data.yaml 有问题时必须在写入项目前给出可读的失败原因。</summary>
    [Theory]
    [InlineData("nc: 3\nnames: ['a', 'b']\n", "不一致")]
    [InlineData("nc: 1\n", "names")]
    [InlineData("names: ['a', 'a']\n", "重复")]
    [InlineData("names: ['a', 'b'\n", "右方括号")]
    [InlineData("names:\n  0: a\n  2: b\n", "连续递增")]
    public void Inspect_RejectsBrokenDataYaml(string yaml, string expectedFragment)
    {
        var exception = Assert.Throws<InvalidDataException>(() => InspectDataYamlPackage(yaml));

        Assert.Contains(expectedFragment, exception.Message, StringComparison.Ordinal);
    }

    /// <summary>同时存在 classes.txt 与 data.yaml 时，以本应用导出的 classes.txt 为准。</summary>
    [Fact]
    public void Inspect_PrefersClassesTxtOverDataYaml()
    {
        var files = new[]
        {
            new ExportFile("data.yaml", Encoding.UTF8.GetBytes("nc: 1\nnames: ['FromYaml']\n")),
            new ExportFile("classes.txt", Encoding.UTF8.GetBytes("0 FromClassesTxt\n")),
            new ExportFile("images/1.jpg", new byte[] { 1, 2, 3 }),
            new ExportFile("labels/1.txt", Encoding.UTF8.GetBytes("0 0.5 0.5 0.2 0.2\n")),
        };
        using var stream = new MemoryStream(ZipHelper.Pack(files));
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read);

        var plan = YoloWithImagesImporter.Inspect(archive);

        Assert.Equal(new[] { "FromClassesTxt" }, plan.Classes);
    }

    /// <summary>重复路径的图片会让“这张图用哪份标注”不可判定，必须拒绝。</summary>
    [Fact]
    public void Inspect_RejectsDuplicateImageEntry()
    {
        var files = new[]
        {
            new ExportFile("classes.txt", Encoding.UTF8.GetBytes("0 part\n")),
            new ExportFile("images/1.jpg", new byte[] { 1, 2, 3 }),
            new ExportFile("images/1.jpg", new byte[] { 4, 5, 6 }),
            new ExportFile("labels/1.txt", Encoding.UTF8.GetBytes("0 0.5 0.5 0.2 0.2\n")),
        };
        using var stream = new MemoryStream(ZipHelper.Pack(files));
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read);

        var exception = Assert.Throws<InvalidDataException>(() => YoloWithImagesImporter.Inspect(archive));

        Assert.Contains("重复路径", exception.Message, StringComparison.Ordinal);
    }

    /// <summary>重复的描述文件只在内容完全一致时才容忍，内容不一致说明包本身有问题。</summary>
    [Fact]
    public void Inspect_RejectsConflictingDuplicateDescriptor()
    {
        var files = new[]
        {
            new ExportFile("data.yaml", Encoding.UTF8.GetBytes("nc: 1\nnames: ['a']\n")),
            new ExportFile("data.yaml", Encoding.UTF8.GetBytes("nc: 1\nnames: ['b']\n")),
            new ExportFile("images/1.jpg", new byte[] { 1, 2, 3 }),
            new ExportFile("labels/1.txt", Encoding.UTF8.GetBytes("0 0.5 0.5 0.2 0.2\n")),
        };
        using var stream = new MemoryStream(ZipHelper.Pack(files));
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read);

        var exception = Assert.Throws<InvalidDataException>(() => YoloWithImagesImporter.Inspect(archive));

        Assert.Contains("内容不一致", exception.Message, StringComparison.Ordinal);
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

    /// <summary>构造一个最小的 Roboflow 形态数据集（data.yaml + train 分目录）并执行自检。</summary>
    private static YoloImportPlan InspectDataYamlPackage(string dataYaml)
    {
        var files = new List<ExportFile>
        {
            new("data.yaml", Encoding.UTF8.GetBytes(dataYaml)),
            new("train/images/a.jpg", new byte[] { 1, 2, 3 }),
            new("train/labels/a.txt", Encoding.UTF8.GetBytes("0 0.5 0.5 0.2 0.2\n")),
        };
        using var stream = new MemoryStream(ZipHelper.Pack(files));
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read);
        return YoloWithImagesImporter.Inspect(archive);
    }
}
