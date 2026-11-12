namespace Snet.Yolo.Tasks.Core.History;

/// <summary>一个工程全部训练记录的汇总统计，显示在训练指标页顶部。</summary>
public sealed class TrainingHistorySummary
{
    /// <summary>训练总次数。</summary>
    public int TotalRuns { get; init; }

    /// <summary>成功完成的次数。</summary>
    public int CompletedRuns { get; init; }

    /// <summary>失败次数。</summary>
    public int FailedRuns { get; init; }

    /// <summary>被取消的次数。</summary>
    public int CancelledRuns { get; init; }

    /// <summary>模型已导出但校准未通过的次数（仅 Anomalib）。</summary>
    public int CalibrationFailedRuns { get; init; }

    /// <summary>成功率，取值 0 至 1；没有训练记录时为 0。</summary>
    public double SuccessRate => TotalRuns > 0 ? (double)CompletedRuns / TotalRuns : 0;

    /// <summary>效果最好的那次训练的标识；没有可比较的模型时为空。</summary>
    public string? BestRunId { get; init; }

    /// <summary>
    /// Anomalib 有多个模型在校准门禁给出的全部指标上并列（例如都是 0 误报）：此时没有最佳，
    /// 因为门禁只能看出误报，看不出检出能力，请用已知异常图对比。
    /// </summary>
    public bool BestIsUndecidable { get; init; }

    /// <summary>最近一次训练的标识。</summary>
    public string? LatestRunId { get; init; }

    /// <summary>最近一次训练的结束时间（UTC）。</summary>
    public DateTime? LatestFinishedAt { get; init; }

    /// <summary>全部训练累计耗时（秒）。</summary>
    public double TotalDurationSeconds { get; init; }

    /// <summary>平均每次训练耗时（秒）。</summary>
    public double AverageDurationSeconds => TotalRuns > 0 ? TotalDurationSeconds / TotalRuns : 0;

    /// <summary>仍保留着模型文件的训练次数。</summary>
    public int RunsWithModel { get; init; }

    /// <summary>保留的模型文件总大小（字节）。</summary>
    public long ModelBytes { get; init; }
}
