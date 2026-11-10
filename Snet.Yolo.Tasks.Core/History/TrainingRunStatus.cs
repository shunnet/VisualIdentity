namespace Snet.Yolo.Tasks.Core.History;

/// <summary>一次训练最终的结果状态，写入训练记录后不再变化。</summary>
public enum TrainingRunStatus
{
    /// <summary>训练完成：YOLO 产出了 best.pt；Anomalib 通过了一致性与正常图误报门禁。</summary>
    Complete = 0,

    /// <summary>训练失败，没有可用的模型。</summary>
    Failed = 1,

    /// <summary>用户停止或服务停止了训练；YOLO 可能留有已保存的阶段性权重。</summary>
    Cancelled = 2,

    /// <summary>Anomalib 模型已完整导出，但校准门禁未通过；仍可下载后人工验收。</summary>
    CalibrationFailed = 3,
}
