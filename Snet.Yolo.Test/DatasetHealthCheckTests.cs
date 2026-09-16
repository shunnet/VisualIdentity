using Snet.Yolo.Tasks.Core.Training;
using Xunit;

namespace Snet.Yolo.Test;

/// <summary>训练前数据集体检与训练后自检（"训练完识别不到"的提前预警）。</summary>
public sealed class DatasetHealthCheckTests
{
    private static readonly string[] NineClasses = { "青叶", "梗", "纸", "麻绳", "青叶切丝", "sponge", "烟茎", "虫茧", "塑料" };

    private static string NewTempDir()
    {
        var directory = Path.Combine(Path.GetTempPath(), "Snet.Yolo.Test", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        return directory;
    }

    private static void WriteLabel(string directory, string name, params string[] lines)
    {
        Directory.CreateDirectory(directory);
        File.WriteAllLines(Path.Combine(directory, name), lines);
    }

    [Fact]
    public void AnalyzeLabels_CountsInstancesAndConvertsBoxSizeToInputPixels()
    {
        var root = NewTempDir();
        try
        {
            var labels = Path.Combine(root, "labels");
            var valLabels = Path.Combine(root, "val", "labels");
            // 框 0.5 x 0.25（归一化）→ 短边 0.25 × 640 = 160 像素
            WriteLabel(labels, "1.txt", "0 0.5 0.5 0.5 0.25", "2 0.1 0.1 0.2 0.2");
            WriteLabel(labels, "2.txt", "0 0.5 0.5 0.5 0.25");
            WriteLabel(valLabels, "3.txt", "1 0.5 0.5 0.4 0.4");

            var stats = DatasetHealthCheck.AnalyzeLabels(labels, valLabels, NineClasses, 640, imageCount: 3);

            Assert.Equal(3, stats.ImageCount);
            Assert.Equal(4, stats.BoxCount);
            Assert.Equal(1, stats.ValImageCount);
            Assert.Equal(2, stats.InstancesPerClass[0]);
            Assert.Equal(1, stats.InstancesPerClass[1]);
            Assert.Equal(1, stats.InstancesPerClass[2]);
            Assert.Equal(0, stats.InstancesPerClass[3]);
            Assert.Equal(6, stats.EmptyClasses);
            Assert.Equal(3, stats.SparseClasses);
            Assert.Equal(160d, stats.MedianBoxPixels, 3);
            Assert.Equal(128d, stats.MinBoxPixels, 3);
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public void AnalyzeLabels_PrefersExplicitImageCount_BecauseUnlabeledImagesHaveNoLabelFile()
    {
        var root = NewTempDir();
        try
        {
            var labels = Path.Combine(root, "labels");
            WriteLabel(labels, "1.txt", "0 0.5 0.5 0.5 0.25");

            var inferred = DatasetHealthCheck.AnalyzeLabels(labels, null, NineClasses, 640);
            var explicitCount = DatasetHealthCheck.AnalyzeLabels(labels, null, NineClasses, 640, imageCount: 23);

            Assert.Equal(1, inferred.ImageCount);
            Assert.Equal(23, explicitCount.ImageCount);
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public void AnalyzeLabels_IgnoresMalformedLinesAndOutOfRangeClassIndex()
    {
        var root = NewTempDir();
        try
        {
            var labels = Path.Combine(root, "labels");
            WriteLabel(labels, "1.txt", "", "0 0.5 0.5", "99 0.5 0.5 0.2 0.2", "abc 0.5 0.5 0.2 0.2", "0 0.5 0.5 0.2 0.2");

            var stats = DatasetHealthCheck.AnalyzeLabels(labels, null, NineClasses, 640);

            Assert.Equal(1, stats.BoxCount);
            Assert.Equal(1, stats.InstancesPerClass[0]);
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public void Warnings_ReportSparseClassesTinyBoxesAndTinyValidationSet()
    {
        var root = NewTempDir();
        try
        {
            var labels = Path.Combine(root, "labels");
            // 每类 1 个实例、框短边 0.01 × 640 = 6.4 像素
            for (var i = 0; i < NineClasses.Length; i++) { WriteLabel(labels, i + ".txt", i + " 0.5 0.5 0.02 0.01"); }

            var stats = DatasetHealthCheck.AnalyzeLabels(labels, null, NineClasses, 640, imageCount: 9);
            var warnings = DatasetHealthCheck.Warnings(stats, 640, 50);

            Assert.Contains(warnings, w => w.Contains("标注太少", StringComparison.Ordinal));
            Assert.Contains(warnings, w => w.Contains("偏小", StringComparison.Ordinal));
            Assert.Contains(warnings, w => w.Contains("验证集为空", StringComparison.Ordinal));
            Assert.Contains(warnings, w => w.Contains("图片只有 9 张", StringComparison.Ordinal));
            Assert.Contains(warnings, w => w.Contains("只有 9 个", StringComparison.Ordinal));
            Assert.Contains(warnings, w => w.Contains("训练轮数", StringComparison.Ordinal));
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public void Warnings_ReportClassesWithoutAnyAnnotation()
    {
        var root = NewTempDir();
        try
        {
            var labels = Path.Combine(root, "labels");
            WriteLabel(labels, "1.txt", "0 0.5 0.5 0.2 0.2");

            var stats = DatasetHealthCheck.AnalyzeLabels(labels, null, NineClasses, 640, imageCount: 60);
            var warnings = DatasetHealthCheck.Warnings(stats, 640, 300);

            Assert.Contains(warnings, w => w.Contains("一个标注都没有", StringComparison.Ordinal) && w.Contains("梗", StringComparison.Ordinal));
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public void Warnings_EmptyForHealthyDataset()
    {
        var root = NewTempDir();
        try
        {
            var labels = Path.Combine(root, "labels");
            var valLabels = Path.Combine(root, "val", "labels");
            // 60 张图、每类 20 个实例、框短边 0.2 × 640 = 128 像素、验证集 6 张
            for (var i = 0; i < 54; i++)
            {
                var lines = new List<string>();
                for (var k = 0; k < 3; k++) { lines.Add(((i * 3 + k) % 9) + " 0.5 0.5 0.3 0.2"); }
                WriteLabel(labels, i + ".txt", lines.ToArray());
            }
            for (var i = 0; i < 6; i++) { WriteLabel(valLabels, "v" + i + ".txt", i + " 0.5 0.5 0.3 0.2"); }

            var stats = DatasetHealthCheck.AnalyzeLabels(labels, valLabels, NineClasses, 640, imageCount: 60);
            var warnings = DatasetHealthCheck.Warnings(stats, 640, 300);

            // 训练集每类 18 个 + 验证集 1 个
            Assert.Equal(19, stats.InstancesPerClass[0]);
            Assert.Equal(60, stats.ImageCount);
            Assert.Empty(warnings);
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public void Warnings_BoxSizeThreshold_SeparatesSmallFromAcceptableTargets()
    {
        var root = NewTempDir();
        try
        {
            // 12.16 像素（0.019 × 640）不报警；11.84 像素（0.0185 × 640）报警
            var okLabels = Path.Combine(root, "ok", "labels");
            WriteLabel(okLabels, "1.txt", "0 0.5 0.5 0.5 0.019");
            var ok = DatasetHealthCheck.AnalyzeLabels(okLabels, null, NineClasses, 640, imageCount: 60);
            Assert.DoesNotContain(DatasetHealthCheck.Warnings(ok, 640, 300), w => w.Contains("偏小", StringComparison.Ordinal));

            var smallLabels = Path.Combine(root, "small", "labels");
            WriteLabel(smallLabels, "1.txt", "0 0.5 0.5 0.5 0.0185");
            var small = DatasetHealthCheck.AnalyzeLabels(smallLabels, null, NineClasses, 640, imageCount: 60);
            Assert.Contains(DatasetHealthCheck.Warnings(small, 640, 300), w => w.Contains("偏小", StringComparison.Ordinal));
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public void AnalyzeClassify_CountsImagesPerClassDirectory()
    {
        var root = NewTempDir();
        try
        {
            var train = Path.Combine(root, "train");
            var val = Path.Combine(root, "val");
            Directory.CreateDirectory(Path.Combine(train, NineClasses[0]));
            Directory.CreateDirectory(Path.Combine(train, NineClasses[1]));
            Directory.CreateDirectory(Path.Combine(val, NineClasses[0]));
            File.WriteAllText(Path.Combine(train, NineClasses[0], "a.jpg"), "x");
            File.WriteAllText(Path.Combine(train, NineClasses[0], "b.jpg"), "x");
            File.WriteAllText(Path.Combine(train, NineClasses[1], "c.jpg"), "x");
            File.WriteAllText(Path.Combine(val, NineClasses[0], "d.jpg"), "x");

            var stats = DatasetHealthCheck.AnalyzeClassify(train, val, NineClasses);

            Assert.False(stats.HasBoxes);
            Assert.Equal(4, stats.ImageCount);
            Assert.Equal(3, stats.InstancesPerClass[0]);
            Assert.Equal(1, stats.InstancesPerClass[1]);
            Assert.Equal(0, stats.InstancesPerClass[2]);
            Assert.Equal(1, stats.ValImageCount);
            Assert.DoesNotContain(DatasetHealthCheck.Warnings(stats, 640, 300), w => w.Contains("偏小", StringComparison.Ordinal));
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public void Summary_MentionsCountsAndBoxSize()
    {
        var root = NewTempDir();
        try
        {
            var labels = Path.Combine(root, "labels");
            WriteLabel(labels, "1.txt", "0 0.5 0.5 0.3 0.2");

            var stats = DatasetHealthCheck.AnalyzeLabels(labels, null, NineClasses, 640, imageCount: 1);
            var summary = DatasetHealthCheck.Summary(stats, 640);

            Assert.Contains("1 张图片", summary, StringComparison.Ordinal);
            Assert.Contains("1 个标注", summary, StringComparison.Ordinal);
            Assert.Contains("青叶 1", summary, StringComparison.Ordinal);
            Assert.Contains("imgsz=640", summary, StringComparison.Ordinal);
        }
        finally { Directory.Delete(root, true); }
    }
}
