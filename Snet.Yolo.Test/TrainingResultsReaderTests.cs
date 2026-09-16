using Snet.Yolo.Tasks.Core.Training;
using Xunit;

namespace Snet.Yolo.Test;

/// <summary>训练后自检：从 results.csv 判断这次训练到底有没有学到东西。</summary>
public sealed class TrainingResultsReaderTests
{
    private const string Header = "epoch,time,train/box_loss,train/cls_loss,train/dfl_loss,metrics/precision(B),metrics/recall(B),metrics/mAP50(B),metrics/mAP50-95(B),val/box_loss,val/cls_loss,val/dfl_loss,lr/pg0";

    private static string WriteCsv(params string[] rows)
    {
        var directory = Path.Combine(Path.GetTempPath(), "Snet.Yolo.Test", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "results.csv");
        File.WriteAllLines(path, new[] { Header }.Concat(rows));
        return path;
    }

    private static string Row(int epoch, double precision, double recall, double map50, double map5095)
        => $"{epoch},1.0,2.0,3.0,0.5,{precision.ToString(System.Globalization.CultureInfo.InvariantCulture)},{recall.ToString(System.Globalization.CultureInfo.InvariantCulture)},{map50.ToString(System.Globalization.CultureInfo.InvariantCulture)},{map5095.ToString(System.Globalization.CultureInfo.InvariantCulture)},1,2,3,0.001";

    [Fact]
    public void Read_TakesBestEpoch_NotLastRow()
    {
        var path = WriteCsv(Row(1, 0, 0, 0, 0), Row(2, 0.5, 0.4, 0.31, 0.12), Row(3, 0, 0, 0.05, 0.01));
        try
        {
            var summary = TrainingResultsReader.Read(path);

            Assert.NotNull(summary);
            Assert.Equal(3, summary!.Epochs);
            Assert.Equal(0.31, summary.Map50, 4);
            Assert.Equal(0.12, summary.Map5095, 4);
            Assert.Equal(0.5, summary.Precision, 4);
            Assert.Equal(0.4, summary.Recall, 4);
        }
        finally { Directory.Delete(Path.GetDirectoryName(path)!, true); }
    }

    [Fact]
    public void Read_TolerantToSpacesAndBlankLines()
    {
        var path = WriteCsv(" ", Row(1, 0, 0, 0.2, 0.1).Replace(",", ", "));
        try
        {
            var summary = TrainingResultsReader.Read(path);

            Assert.NotNull(summary);
            Assert.Equal(1, summary!.Epochs);
            Assert.Equal(0.2, summary.Map50, 4);
        }
        finally { Directory.Delete(Path.GetDirectoryName(path)!, true); }
    }

    [Fact]
    public void Read_ReturnsNull_WhenFileMissingHeaderOrRows()
    {
        Assert.Null(TrainingResultsReader.Read(Path.Combine(Path.GetTempPath(), "no-such-results.csv")));

        var directory = Path.Combine(Path.GetTempPath(), "Snet.Yolo.Test", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var wrongHeader = Path.Combine(directory, "no-map.csv");
            File.WriteAllLines(wrongHeader, new[] { "epoch,time,train/box_loss", "1,1.0,2.0" });
            Assert.Null(TrainingResultsReader.Read(wrongHeader));

            var headerOnly = Path.Combine(directory, "header-only.csv");
            File.WriteAllLines(headerOnly, new[] { Header });
            Assert.Null(TrainingResultsReader.Read(headerOnly));
        }
        finally { Directory.Delete(directory, true); }
    }

    [Fact]
    public void LearnedNothing_IsTrueOnlyForZeroMap50()
    {
        var zero = WriteCsv(Row(1, 0, 0, 0, 0), Row(2, 0, 0, 0, 0));
        var some = WriteCsv(Row(1, 0, 0, 0.4, 0.2));
        try
        {
            Assert.True(TrainingResultsReader.LearnedNothing(TrainingResultsReader.Read(zero)));
            Assert.False(TrainingResultsReader.LearnedNothing(TrainingResultsReader.Read(some)));
            Assert.False(TrainingResultsReader.LearnedNothing(null));
        }
        finally
        {
            Directory.Delete(Path.GetDirectoryName(zero)!, true);
            Directory.Delete(Path.GetDirectoryName(some)!, true);
        }
    }

    [Fact]
    public void Describe_IncludesRoundedMetrics()
    {
        var path = WriteCsv(Row(1, 0.0001, 0.1, 0.01, 0.001), Row(2, 0.0012, 0.5, 0.1234, 0.0456), Row(3, 0.0002, 0.2, 0.05, 0.01));
        try
        {
            var summary = TrainingResultsReader.Read(path);
            var text = TrainingResultsReader.Describe(summary!);

            Assert.Contains("3 轮", text, StringComparison.Ordinal);
            Assert.Contains("mAP50 = 0.1234", text, StringComparison.Ordinal);
            Assert.Contains("mAP50-95 = 0.0456", text, StringComparison.Ordinal);
            Assert.Contains("召回率 = 0.5", text, StringComparison.Ordinal);
        }
        finally { Directory.Delete(Path.GetDirectoryName(path)!, true); }
    }

    [Fact]
    public void ExplainLearnedNothing_UsesDatasetStatsAndEpochs()
    {
        var stats = new DatasetStats(23, 15, new[] { "青叶", "梗", "纸" }, new[] { 1, 5, 9 }, new[] { 20d, 20d }, 2, HasBoxes: true);
        var text = TrainingResultsReader.ExplainLearnedNothing(stats, 50);

        Assert.Contains("mAP50 为 0", text, StringComparison.Ordinal);
        Assert.Contains("标注太少的类别有 3 个", text, StringComparison.Ordinal);
        Assert.Contains("图片只有 23 张", text, StringComparison.Ordinal);
        Assert.Contains("只有 15 个", text, StringComparison.Ordinal);
        Assert.Contains("当前 50 轮", text, StringComparison.Ordinal);
        Assert.Contains("验证集只有 2 张", text, StringComparison.Ordinal);
    }

    [Fact]
    public void ExplainLearnedNothing_HandlesMissingStats()
    {
        var text = TrainingResultsReader.ExplainLearnedNothing(null, 100);

        Assert.Contains("mAP50 为 0", text, StringComparison.Ordinal);
        Assert.Contains("当前 100 轮", text, StringComparison.Ordinal);
        Assert.DoesNotContain("验证集只有", text, StringComparison.Ordinal);
    }
}
