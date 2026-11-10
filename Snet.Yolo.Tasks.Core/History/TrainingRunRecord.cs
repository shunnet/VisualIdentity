namespace Snet.Yolo.Tasks.Core.History;

using Snet.Yolo.Server.models;
using System.Text.Json.Serialization;

/// <summary>
/// 一次训练的档案：不论训练成功、失败还是被取消都会保存，包含配置、指标、验证结果与模型位置。
/// 训练指标页据此展示汇总、对比、导出和删除；使用 record 以便在模型文件被删除后用 <c>with</c> 生成去掉模型路径的副本。
/// </summary>
public sealed record TrainingRunRecord
{
    /// <summary>训练运行标识：Anomalib 为 <c>anomalib-run-&lt;guid&gt;</c>，YOLO 为 <c>yolo-&lt;时间戳&gt;-&lt;随机串&gt;</c>。</summary>
    public required string Id { get; init; }

    /// <summary>所属工程标识。</summary>
    public required string ProjectId { get; init; }

    /// <summary>训练算法族。</summary>
    public required ProjectKind Kind { get; init; }

    /// <summary>最终状态。</summary>
    public TrainingRunStatus Status { get; init; }

    /// <summary>开始时间（UTC）。</summary>
    public DateTime StartedAt { get; init; }

    /// <summary>结束时间（UTC）。</summary>
    public DateTime FinishedAt { get; init; }

    /// <summary>训练模型：YOLO 为 <c>yolo26n.pt</c> 等，Anomalib 为 PaDiM / EfficientAD Small / PatchCore。</summary>
    public string Algorithm { get; init; } = string.Empty;

    /// <summary>任务类型：detect、segment、classify、pose、obb 或 anomaly。</summary>
    public string Task { get; init; } = string.Empty;

    /// <summary>输入图像边长。</summary>
    public int ImageSize { get; init; }

    /// <summary>计划训练轮数。</summary>
    public int Epochs { get; init; }

    /// <summary>实际完成的轮数。</summary>
    public int CompletedEpochs { get; init; }

    /// <summary>训练设备参数，如 <c>0</c>、<c>cuda:0</c>、<c>cpu</c>。</summary>
    public string Device { get; init; } = string.Empty;

    /// <summary>训练所用显卡名称；CPU 训练为空。</summary>
    public string GpuName { get; init; } = string.Empty;

    /// <summary>完成说明或失败原因。</summary>
    public string Message { get; init; } = string.Empty;

    /// <summary>用于训练的图片数。</summary>
    public int TrainImages { get; init; }

    /// <summary>验证图片数：YOLO 为验证集，Anomalib 为独立校准集。</summary>
    public int ValidationImages { get; init; }

    /// <summary>类别数（YOLO）；Anomalib 为 0。</summary>
    public int ClassCount { get; init; }

    /// <summary>用于排序和选出最佳模型的主指标值；没有指标时为空。</summary>
    public double? PrimaryScore { get; init; }

    /// <summary>主指标名称，如 mAP50-95、accuracy、falsePositiveRate。</summary>
    public string PrimaryMetric { get; init; } = string.Empty;

    /// <summary>YOLO 专有指标；Anomalib 记录为空。</summary>
    public YoloRunDetails? Yolo { get; init; }

    /// <summary>Anomalib 专有指标；YOLO 记录为空。</summary>
    public AnomalibRunDetails? Anomalib { get; init; }

    /// <summary>本次运行的输出目录，相对于该工程的训练目录。</summary>
    public string RunDirectory { get; init; } = string.Empty;

    /// <summary>模型文件相对于工程训练目录的路径：YOLO 为 best.pt，Anomalib 为 model.onnx；没有模型时为空。</summary>
    public string ModelPath { get; init; } = string.Empty;

    /// <summary>模型文件大小（字节）。</summary>
    public long ModelBytes { get; init; }

    /// <summary>训练耗时（秒）。</summary>
    [JsonIgnore]
    public double DurationSeconds => Math.Max(0, (FinishedAt - StartedAt).TotalSeconds);

    /// <summary>是否保留了可导出的模型文件。</summary>
    [JsonIgnore]
    public bool HasModel => !string.IsNullOrEmpty(ModelPath);
}
