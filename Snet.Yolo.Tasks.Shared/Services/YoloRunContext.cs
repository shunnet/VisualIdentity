namespace Snet.Yolo.Tasks.Services;

using Snet.Yolo.Tasks.Core.Training;

/// <summary>一次 YOLO 训练在流水线各阶段逐步积累的上下文，训练结束时据此生成训练记录。</summary>
public sealed class YoloRunContext
{
    /// <summary>训练运行标识。</summary>
    public required string RunId { get; init; }

    /// <summary>工程标识。</summary>
    public required string ProjectId { get; init; }

    /// <summary>本次训练开始时间（UTC）。</summary>
    public DateTime StartedAt { get; init; } = DateTime.UtcNow;

    /// <summary>本次训练使用的选项（设备在环境检测后会被改写为实际值）。</summary>
    public required TrainingOptions Options { get; init; }

    /// <summary>工程训练目录的绝对路径；数据集导出前为空。</summary>
    public string? ProjectDirectory { get; set; }

    /// <summary>数据集体检统计；导出前或失败时为空。</summary>
    public DatasetStats? Stats { get; set; }

    /// <summary>定位到的 best.pt 绝对路径；失败或未产出时为空。</summary>
    public string? BestModelPath { get; set; }
}
