using Snet.Yolo.Server.models;
using Snet.Yolo.Tasks.Core.History;
using Xunit;

namespace Snet.Yolo.Test;

/// <summary>训练记录汇总与“效果最好”判定规则。</summary>
public sealed class TrainingHistoryAnalyzerTests
{
    private static readonly DateTime Start = new(2026, 11, 10, 8, 0, 0, DateTimeKind.Utc);

    /// <summary>构造一条 YOLO 记录。</summary>
    private static TrainingRunRecord Yolo(string id, TrainingRunStatus status, double? score, int minutes, bool hasModel = true, bool validated = false)
        => new()
        {
            Id = id, ProjectId = "p", Kind = ProjectKind.Yolo, Status = status,
            StartedAt = Start.AddMinutes(minutes), FinishedAt = Start.AddMinutes(minutes + 10),
            PrimaryScore = score, PrimaryMetric = "mAP50-95",
            ModelPath = hasModel ? "runs/" + id + "/weights/best.pt" : string.Empty, ModelBytes = hasModel ? 1000 : 0,
            Yolo = new YoloRunDetails { UsedValidationSet = validated },
        };

    /// <summary>构造一条 Anomalib 记录。</summary>
    private static TrainingRunRecord Anomalib(string id, TrainingRunStatus status, int normal, int falsePositives, int total, int passed, double? auroc, int minutes)
        => new()
        {
            Id = id, ProjectId = "p", Kind = ProjectKind.Anomalib, Status = status,
            StartedAt = Start.AddMinutes(minutes), FinishedAt = Start.AddMinutes(minutes + 5),
            ModelPath = id + "/artifacts/model.onnx", ModelBytes = 2000,
            Anomalib = new AnomalibRunDetails
            {
                ExportSucceeded = true, NormalSamples = normal, FalsePositives = falsePositives, TotalSamples = total, PassedSamples = passed, BestPixelAuroc = auroc,
            },
        };

    /// <summary>YOLO 选 mAP50-95 最高的成功训练，并列取最新。</summary>
    [Fact]
    public void SelectBest_Yolo_PicksHighestScoreThenNewest()
    {
        var runs = new[]
        {
            Yolo("a", TrainingRunStatus.Complete, 0.60, 0),
            Yolo("b", TrainingRunStatus.Complete, 0.80, 20),
            Yolo("c", TrainingRunStatus.Complete, 0.80, 40),
            Yolo("d", TrainingRunStatus.Failed, 0.95, 60),
        };
        Assert.Equal("c", TrainingHistoryAnalyzer.SelectBest(runs)?.Id);
    }

    /// <summary>YOLO 优先选用了验证集的训练：没有验证集时指标在训练集上计算，偏乐观，不能压过有验证集的结果。</summary>
    [Fact]
    public void SelectBest_Yolo_PrefersRunsThatUsedAValidationSet()
    {
        var runs = new[]
        {
            Yolo("train-set-only", TrainingRunStatus.Complete, 0.95, 20, validated: false),
            Yolo("validated", TrainingRunStatus.Complete, 0.70, 0, validated: true),
        };
        Assert.Equal("validated", TrainingHistoryAnalyzer.SelectBest(runs)?.Id);
    }

    /// <summary>没有成功的训练时，仍在带模型的取消训练中选最好的一条。</summary>
    [Fact]
    public void SelectBest_Yolo_FallsBackToCancelledRuns()
    {
        var runs = new[] { Yolo("a", TrainingRunStatus.Cancelled, 0.4, 0), Yolo("b", TrainingRunStatus.Cancelled, 0.5, 20) };
        Assert.Equal("b", TrainingHistoryAnalyzer.SelectBest(runs)?.Id);
    }

    /// <summary>模型文件已被删除的记录不参与评选；没有任何可比较模型时没有最佳。</summary>
    [Fact]
    public void SelectBest_IgnoresRunsWithoutModel()
    {
        var runs = new[] { Yolo("a", TrainingRunStatus.Complete, 0.9, 0, hasModel: false) };
        Assert.Null(TrainingHistoryAnalyzer.SelectBest(runs));
        Assert.Null(TrainingHistoryAnalyzer.SelectBest([]));
    }

    /// <summary>Anomalib 优先通过门禁的，再比误报率、通过率；合成验证 AUROC 不参与排序。</summary>
    [Fact]
    public void SelectBest_Anomalib_PrefersGatePassedThenFewestFalsePositives()
    {
        var runs = new[]
        {
            Anomalib("failed-gate", TrainingRunStatus.CalibrationFailed, 15, 0, 15, 15, 0.99, 0),
            Anomalib("ok-fp1", TrainingRunStatus.Complete, 15, 1, 15, 14, 0.80, 10),
            Anomalib("ok-fp0-high-auroc", TrainingRunStatus.Complete, 15, 0, 15, 15, 0.90, 20),
        };
        Assert.Equal("ok-fp0-high-auroc", TrainingHistoryAnalyzer.SelectBest(runs)?.Id);
        Assert.False(TrainingHistoryAnalyzer.Summarize(runs).BestIsUndecidable);
    }

    /// <summary>
    /// 两条零误报、全部通过的训练在门禁的全部指标上并列：不能拿“最新”冒充“最好”。
    /// 门禁只数正常图的误报，几乎什么都不报的模型也是 0 误报（EfficientAD 768 px 就这样被标成了最佳）。
    /// </summary>
    [Fact]
    public void SelectBest_Anomalib_TiedTopRunsHaveNoBest()
    {
        var runs = new[]
        {
            Anomalib("patchcore", TrainingRunStatus.Complete, 15, 0, 15, 15, null, 0),
            Anomalib("efficientad-with-auroc", TrainingRunStatus.Complete, 15, 0, 15, 15, 0.82, 120),
        };

        var summary = TrainingHistoryAnalyzer.Summarize(runs);

        Assert.Null(TrainingHistoryAnalyzer.SelectBest(runs));
        Assert.Null(summary.BestRunId);
        Assert.True(summary.BestIsUndecidable);
        Assert.Equal("efficientad-with-auroc", summary.LatestRunId);
    }

    /// <summary>只有一条可比较的 Anomalib 模型时它就是最佳；并列但都在后面的名次不影响最前面的那条。</summary>
    [Fact]
    public void SelectBest_Anomalib_SingleRunIsBest_AndTiesBelowTheTopDoNotMatter()
    {
        Assert.Equal("only", TrainingHistoryAnalyzer.SelectBest([Anomalib("only", TrainingRunStatus.Complete, 15, 0, 15, 15, null, 0)])?.Id);

        var runs = new[]
        {
            Anomalib("clear-winner", TrainingRunStatus.Complete, 15, 0, 15, 15, null, 0),
            Anomalib("tie-a", TrainingRunStatus.Complete, 15, 1, 15, 14, null, 10),
            Anomalib("tie-b", TrainingRunStatus.Complete, 15, 1, 15, 14, null, 20),
        };
        Assert.Equal("clear-winner", TrainingHistoryAnalyzer.SelectBest(runs)?.Id);
        Assert.False(TrainingHistoryAnalyzer.Summarize(runs).BestIsUndecidable);
    }

    /// <summary>汇总统计各状态次数、成功率、累计耗时与模型占用。</summary>
    [Fact]
    public void Summarize_CountsStatusesAndModelStorage()
    {
        var runs = new[]
        {
            Yolo("a", TrainingRunStatus.Complete, 0.7, 0),
            Yolo("b", TrainingRunStatus.Failed, null, 20, hasModel: false),
            Yolo("c", TrainingRunStatus.Cancelled, 0.3, 40),
        };
        var summary = TrainingHistoryAnalyzer.Summarize(runs);
        Assert.Equal(3, summary.TotalRuns);
        Assert.Equal(1, summary.CompletedRuns);
        Assert.Equal(1, summary.FailedRuns);
        Assert.Equal(1, summary.CancelledRuns);
        Assert.Equal(1d / 3, summary.SuccessRate, 6);
        Assert.Equal(1800, summary.TotalDurationSeconds);
        Assert.Equal(600, summary.AverageDurationSeconds);
        Assert.Equal(2, summary.RunsWithModel);
        Assert.Equal(2000, summary.ModelBytes);
        Assert.Equal("a", summary.BestRunId);
        Assert.Equal("c", summary.LatestRunId);
    }

    /// <summary>空列表的汇总不抛异常，成功率为 0。</summary>
    [Fact]
    public void Summarize_EmptyList_IsAllZero()
    {
        var summary = TrainingHistoryAnalyzer.Summarize([]);
        Assert.Equal(0, summary.TotalRuns);
        Assert.Equal(0, summary.SuccessRate);
        Assert.Null(summary.BestRunId);
        Assert.Null(summary.LatestRunId);
    }
}
