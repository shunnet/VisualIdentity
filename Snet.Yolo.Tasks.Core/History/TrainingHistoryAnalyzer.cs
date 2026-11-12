namespace Snet.Yolo.Tasks.Core.History;

using Snet.Yolo.Server.models;

/// <summary>训练记录的汇总与“效果最好”的判定规则，纯函数，便于单元测试。</summary>
public static class TrainingHistoryAnalyzer
{
    /// <summary>
    /// 选出效果最好的一次训练。
    /// YOLO：成功且有模型的训练里，优先使用了验证集的训练（没有验证集时指标在训练集上计算，偏乐观），再按主指标（mAP50-95 或分类准确率）最高，并列取最新；没有成功的训练时在其余带模型的训练里选。
    /// Anomalib：已完整导出模型的训练里，优先通过校准门禁的，其次正常图误报率更低、逐图通过率更高；
    /// 这几项相同（例如都是 0 误报）时没有“最佳”：门禁只数正常图的误报，一个几乎什么都不报的模型同样得满分，
    /// 此时不能拿“最新”冒充“最好”（曾经把检出能力差得多的 EfficientAD 标成最佳）。
    /// 验证像素 AUROC 来自合成异常且只有 EfficientAD 才有，既不同算法之间不可比、也与真实缺陷效果相关性很弱，因此不参与排序；
    /// Anomalib 训练没有真实缺陷样本，也不据此宣称缺陷检出率。
    /// </summary>
    /// <param name="runs">同一工程的训练记录。</param>
    /// <returns>最佳记录；没有任何可比较的模型时为空。</returns>
    public static TrainingRunRecord? SelectBest(IReadOnlyList<TrainingRunRecord> runs)
    {
        ArgumentNullException.ThrowIfNull(runs);
        var candidates = runs.Where(static run => run.HasModel).ToList();
        if (candidates.Count == 0) { return null; }
        return candidates[0].Kind == ProjectKind.Anomalib ? SelectBestAnomalib(candidates) : SelectBestYolo(candidates);
    }

    /// <summary>汇总一个工程的训练次数、成功率、耗时和模型占用。</summary>
    /// <param name="runs">同一工程的训练记录。</param>
    /// <returns>汇总统计。</returns>
    public static TrainingHistorySummary Summarize(IReadOnlyList<TrainingRunRecord> runs)
    {
        ArgumentNullException.ThrowIfNull(runs);
        var latest = runs.MaxBy(static run => run.FinishedAt);
        return new TrainingHistorySummary
        {
            TotalRuns = runs.Count,
            CompletedRuns = runs.Count(static run => run.Status == TrainingRunStatus.Complete),
            FailedRuns = runs.Count(static run => run.Status == TrainingRunStatus.Failed),
            CancelledRuns = runs.Count(static run => run.Status == TrainingRunStatus.Cancelled),
            CalibrationFailedRuns = runs.Count(static run => run.Status == TrainingRunStatus.CalibrationFailed),
            BestRunId = SelectBest(runs)?.Id,
            BestIsUndecidable = IsAnomalibBestUndecidable(runs),
            LatestRunId = latest?.Id,
            LatestFinishedAt = latest?.FinishedAt,
            TotalDurationSeconds = runs.Sum(static run => run.DurationSeconds),
            RunsWithModel = runs.Count(static run => run.HasModel),
            ModelBytes = runs.Where(static run => run.HasModel).Sum(static run => run.ModelBytes),
        };
    }

    /// <summary>YOLO：成功的训练优先，其次用了验证集的优先，再按主指标降序，最后按结束时间取最新。</summary>
    private static TrainingRunRecord? SelectBestYolo(List<TrainingRunRecord> candidates)
        => candidates
            .Where(static run => run.PrimaryScore.HasValue)
            .OrderByDescending(static run => run.Status == TrainingRunStatus.Complete)
            .ThenByDescending(static run => run.Yolo?.UsedValidationSet == true)
            .ThenByDescending(static run => run.PrimaryScore)
            .ThenByDescending(static run => run.FinishedAt)
            .FirstOrDefault();

    /// <summary>Anomalib：通过门禁优先，再比误报率、通过率；排在最前面的有多个并列时没有最佳。</summary>
    private static TrainingRunRecord? SelectBestAnomalib(List<TrainingRunRecord> candidates)
    {
        var ranked = RankAnomalib(candidates);
        return ranked.Count > 0 && !(ranked.Count > 1 && SameAnomalibRank(ranked[0], ranked[1])) ? ranked[0] : null;
    }

    /// <summary>Anomalib 的最佳是否因为并列而无法判断：至少有两条已导出的模型，且排在最前面的两条并列。</summary>
    private static bool IsAnomalibBestUndecidable(IReadOnlyList<TrainingRunRecord> runs)
    {
        var candidates = runs.Where(static run => run.HasModel).ToList();
        if (candidates.Count == 0 || candidates[0].Kind != ProjectKind.Anomalib) { return false; }
        var ranked = RankAnomalib(candidates);
        return ranked.Count > 1 && SameAnomalibRank(ranked[0], ranked[1]);
    }

    /// <summary>按门禁结果排序，顺序相同的并列时较新的在前（只决定展示顺序，不代表更好）。</summary>
    private static List<TrainingRunRecord> RankAnomalib(IEnumerable<TrainingRunRecord> candidates)
        => candidates
            .Where(static run => run.Anomalib is { ExportSucceeded: true })
            .OrderByDescending(static run => run.Status == TrainingRunStatus.Complete)
            .ThenBy(static run => run.Anomalib!.FalsePositiveRate ?? 1d)
            .ThenByDescending(static run => run.Anomalib!.PassRate ?? 0d)
            .ThenByDescending(static run => run.FinishedAt)
            .ToList();

    /// <summary>两条记录在门禁给出的全部排序依据上完全相同。</summary>
    private static bool SameAnomalibRank(TrainingRunRecord left, TrainingRunRecord right)
        => (left.Status == TrainingRunStatus.Complete) == (right.Status == TrainingRunStatus.Complete)
           && (left.Anomalib!.FalsePositiveRate ?? 1d) == (right.Anomalib!.FalsePositiveRate ?? 1d)
           && (left.Anomalib!.PassRate ?? 0d) == (right.Anomalib!.PassRate ?? 0d);
}
