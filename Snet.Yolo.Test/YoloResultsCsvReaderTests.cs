using Snet.Yolo.Tasks.Core.Training;
using Xunit;

namespace Snet.Yolo.Test;

/// <summary>Ultralytics results.csv 的完整解析：最佳轮次、末轮损失与曲线。</summary>
public sealed class YoloResultsCsvReaderTests : IDisposable
{
    /// <summary>每个测试独立的临时目录。</summary>
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "csv-" + Guid.NewGuid().ToString("N"));

    /// <summary>创建目录。</summary>
    public YoloResultsCsvReaderTests() => Directory.CreateDirectory(_directory);

    /// <summary>删除临时目录。</summary>
    public void Dispose() => Directory.Delete(_directory, recursive: true);

    /// <summary>写入一个 csv 并返回路径。</summary>
    private string Write(string content)
    {
        var path = Path.Combine(_directory, "results.csv");
        File.WriteAllText(path, content);
        return path;
    }

    /// <summary>检测任务：按 fitness 选最佳轮次，指标取该轮，损失取末轮，曲线逐轮。</summary>
    [Fact]
    public void Read_DetectCsv_PicksBestFitnessEpoch()
    {
        var path = Write("""
            epoch,time,train/box_loss,train/cls_loss,train/dfl_loss,metrics/precision(B),metrics/recall(B),metrics/mAP50(B),metrics/mAP50-95(B)
            1,10,2.0,3.0,1.0,0.1,0.2,0.3,0.10
            2,20,1.5,2.0,0.9,0.5,0.6,0.7,0.40
            3,30,1.2,1.5,0.8,0.4,0.5,0.6,0.35
            """);

        var series = YoloResultsCsvReader.Read(path);

        Assert.NotNull(series);
        Assert.Equal(3, series.Epochs);
        Assert.Equal(2, series.BestEpoch);
        Assert.Equal(0.7, series.Map50);
        Assert.Equal(0.40, series.Map5095);
        Assert.Equal(0.5, series.Precision);
        Assert.Equal(0.6, series.Recall);
        Assert.Equal(1.2, series.BoxLoss);
        Assert.Equal("mAP50-95", series.PrimaryMetric);
        Assert.Equal(0.40, series.PrimaryScore);
        Assert.Equal([1, 2, 3], series.Curve.Select(point => point.Epoch));
        Assert.Equal(6.0, series.Curve[0].Loss);
    }

    /// <summary>分类任务没有 mAP 列，主指标为 top1 准确率。</summary>
    [Fact]
    public void Read_ClassifyCsv_UsesTop1Accuracy()
    {
        var path = Write("""
            epoch,time,train/loss,metrics/accuracy_top1,metrics/accuracy_top5,val/loss
            1,5,1.2,0.60,0.9,1.1
            2,10,0.8,0.85,0.95,0.7
            """);

        var series = YoloResultsCsvReader.Read(path);

        Assert.NotNull(series);
        Assert.Equal(2, series.BestEpoch);
        Assert.Equal(0.85, series.Accuracy);
        Assert.Null(series.Map5095);
        Assert.Equal("accuracy", series.PrimaryMetric);
        Assert.Equal(0.85, series.PrimaryScore);
        Assert.Equal(0.8, series.Curve[1].Loss);
    }

    /// <summary>实例分割：fitness 同时计入框与掩码，最佳轮次不能只看框。</summary>
    [Fact]
    public void Read_SegmentCsv_FitnessIncludesMaskMetrics()
    {
        var path = Write("""
            epoch,metrics/mAP50(B),metrics/mAP50-95(B),metrics/mAP50(M),metrics/mAP50-95(M)
            1,0.60,0.50,0.50,0.20
            2,0.55,0.45,0.60,0.40
            """);

        var series = YoloResultsCsvReader.Read(path);

        // 轮次 1：框 0.51 + 掩码 0.23 = 0.74；轮次 2：框 0.46 + 掩码 0.42 = 0.88。只看框会错选轮次 1。
        Assert.NotNull(series);
        Assert.Equal(2, series.BestEpoch);
    }

    /// <summary>分类：fitness 是 top1 与 top5 的均值，不只看 top1。</summary>
    [Fact]
    public void Read_ClassifyCsv_FitnessAveragesTop1AndTop5()
    {
        var path = Write("""
            epoch,train/loss,metrics/accuracy_top1,metrics/accuracy_top5
            1,1.0,0.80,0.90
            2,0.9,0.82,0.80
            """);

        var series = YoloResultsCsvReader.Read(path);

        // 轮次 1：(0.80 + 0.90) / 2 = 0.85；轮次 2：(0.82 + 0.80) / 2 = 0.81。只看 top1 会错选轮次 2。
        Assert.NotNull(series);
        Assert.Equal(1, series.BestEpoch);
        Assert.Equal(0.80, series.Accuracy);
    }

    /// <summary>Ultralytics 为对齐在表头和数值里写入空白，解析时必须去掉。</summary>
    [Fact]
    public void Read_TrimsPaddedHeadersAndValues()
    {
        var path = Write("                  epoch,   metrics/mAP50(B),   metrics/mAP50-95(B)\n                      1,            0.5,           0.25\n");
        var series = YoloResultsCsvReader.Read(path);
        Assert.NotNull(series);
        Assert.Equal(0.25, series.Map5095);
    }

    /// <summary>文件缺失、只有表头或没有任何指标列时返回空。</summary>
    [Theory]
    [InlineData("")]
    [InlineData("epoch,time\n")]
    [InlineData("epoch,time\n1,2\n")]
    public void Read_ReturnsNullWhenNothingUsable(string content)
    {
        Assert.Null(YoloResultsCsvReader.Read(Write(content)));
        Assert.Null(YoloResultsCsvReader.Read(Path.Combine(_directory, "missing.csv")));
    }
}
