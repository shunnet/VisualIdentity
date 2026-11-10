using Snet.Yolo.Server.anomalib;
using Xunit;

namespace Snet.Yolo.Test;

/// <summary>Python 流水线输出的结构化进度行必须正确更新训练页的实时指标。</summary>
public sealed class AnomalibProgressParserTests
{
    /// <summary>构造带固定前缀的一行输出。</summary>
    private static string Line(string json) => AnomalibProgressParser.Marker + json;

    /// <summary>阶段进度更新阶段名称与步数。</summary>
    [Fact]
    public void Progress_UpdatesStageAndSteps()
    {
        var metrics = new AnomalibLiveMetrics();
        Assert.True(AnomalibProgressParser.TryApply(Line("""{"type":"progress","stage":"extract","current":4,"total":20}"""), metrics));
        Assert.Equal("extract", metrics.Stage);
        Assert.Equal(4, metrics.StageCurrent);
        Assert.Equal(20, metrics.StageTotal);
        Assert.True(metrics.HasValue);
    }

    /// <summary>EfficientAD 轮次行更新损失、验证 AUROC，并累积曲线。</summary>
    [Fact]
    public void Epoch_UpdatesLossesAndHistory()
    {
        var metrics = new AnomalibLiveMetrics();
        AnomalibProgressParser.TryApply(Line("""{"type":"epoch","epoch":1,"totalEpochs":500,"trainLoss":3.5,"studentLoss":2.0,"autoencoderLoss":1.5,"pixelAuroc":0.61,"bestPixelAuroc":0.61,"bestEpoch":1}"""), metrics);
        AnomalibProgressParser.TryApply(Line("""{"type":"epoch","epoch":2,"totalEpochs":500,"trainLoss":3.1,"pixelAuroc":0.70,"bestPixelAuroc":0.70,"bestEpoch":2}"""), metrics);

        Assert.Equal(2, metrics.Epoch);
        Assert.Equal(500, metrics.TotalEpochs);
        Assert.Equal(3.1, metrics.TrainLoss);
        Assert.Null(metrics.StudentLoss);
        Assert.Equal(0.70, metrics.BestPixelAuroc);
        Assert.Equal(2, metrics.BestEpoch);
        Assert.Equal([1, 2], metrics.History.Select(point => point.Epoch));
        Assert.Equal(0.61, metrics.History[0].PixelAuroc);
    }

    /// <summary>曲线最多保留固定点数，超出时丢弃最早的点。</summary>
    [Fact]
    public void Epoch_History_IsBounded()
    {
        var metrics = new AnomalibLiveMetrics();
        for (var epoch = 1; epoch <= AnomalibLiveMetrics.MaximumHistoryPoints + 5; epoch++)
        {
            AnomalibProgressParser.TryApply(Line($$"""{"type":"epoch","epoch":{{epoch}},"totalEpochs":9999}"""), metrics);
        }
        Assert.Equal(AnomalibLiveMetrics.MaximumHistoryPoints, metrics.History.Count);
        Assert.Equal(6, metrics.History[0].Epoch);
    }

    /// <summary>PatchCore 的显存规划和阈值标定结果被记录。</summary>
    [Fact]
    public void PlanAndCalibration_AreRecorded()
    {
        var metrics = new AnomalibLiveMetrics();
        AnomalibProgressParser.TryApply(Line("""{"type":"plan","patches":691200,"keepFraction":0.89,"memoryBank":6912,"embeddingMiB":4050,"freeVramMiB":15684}"""), metrics);
        AnomalibProgressParser.TryApply(Line("""{"type":"calibration","sampleCount":27,"imageThreshold":57.7,"imageScoreMax":53.0}"""), metrics);

        Assert.Equal(691200, metrics.PatchCount);
        Assert.Equal(0.89, metrics.KeepFraction);
        Assert.Equal(6912, metrics.MemoryBank);
        Assert.Equal(4050, metrics.EmbeddingMiB);
        Assert.Equal(15684, metrics.FreeVramMiB);
        Assert.Equal(57.7, metrics.ImageThreshold);
        Assert.Equal(53.0, metrics.NormalScoreMax);
    }

    /// <summary>普通日志、损坏的 JSON、未知类型都被忽略且不抛异常；结构化行可被识别以便不进入日志。</summary>
    [Theory]
    [InlineData("训练日志一行")]
    [InlineData("VISUALIDENTITY_JSON:{ broken")]
    [InlineData("""VISUALIDENTITY_JSON:{"type":"unknown"}""")]
    [InlineData("""VISUALIDENTITY_JSON:[1,2]""")]
    [InlineData("""VISUALIDENTITY_JSON:{"type":"epoch","epoch":0}""")]
    public void InvalidLines_AreIgnored(string line)
    {
        var metrics = new AnomalibLiveMetrics();
        Assert.False(AnomalibProgressParser.TryApply(line, metrics));
        Assert.False(metrics.HasValue);
    }

    /// <summary>非有限数值（NaN、无穷）不会进入指标。</summary>
    [Fact]
    public void NonFiniteValues_BecomeNull()
    {
        var metrics = new AnomalibLiveMetrics();
        AnomalibProgressParser.TryApply(Line("""{"type":"epoch","epoch":1,"totalEpochs":10,"trainLoss":null,"pixelAuroc":1e999}"""), metrics);
        Assert.Null(metrics.TrainLoss);
        Assert.Null(metrics.PixelAuroc);
    }

    /// <summary>副本互不影响，曲线列表独立。</summary>
    [Fact]
    public void Clone_IsIndependent()
    {
        var metrics = new AnomalibLiveMetrics();
        AnomalibProgressParser.TryApply(Line("""{"type":"epoch","epoch":1,"totalEpochs":10}"""), metrics);
        var clone = metrics.Clone();
        AnomalibProgressParser.TryApply(Line("""{"type":"epoch","epoch":2,"totalEpochs":10}"""), metrics);
        Assert.Single(clone.History);
        Assert.Equal(2, metrics.History.Count);
    }
}
