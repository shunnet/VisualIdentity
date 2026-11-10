namespace Snet.Yolo.Tasks.Services;

using Snet.Yolo.Tasks.Core.History;

/// <summary>训练指标页一次加载的全部数据：按时间从新到旧排列的记录、汇总统计和最佳记录标识。</summary>
/// <param name="Runs">训练记录；模型文件已被删除的记录不再带模型路径。</param>
/// <param name="Summary">汇总统计。</param>
public sealed record TrainingHistoryView(IReadOnlyList<TrainingRunRecord> Runs, TrainingHistorySummary Summary)
{
    /// <summary>效果最好的那次训练；没有可比较的模型时为空。</summary>
    public TrainingRunRecord? Best => Summary.BestRunId is null ? null : Runs.FirstOrDefault(run => run.Id == Summary.BestRunId);
}
