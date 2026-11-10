namespace Snet.Yolo.Tasks.Core.Training;

/// <summary>训练指标（来自 yolo 日志）。</summary>
public sealed class TrainingMetrics
{
    /// <summary>Mean average precision at IoU 0.50.</summary>
    public double? Map50 { get; set; }
    /// <summary>Mean average precision averaged over IoU 0.50–0.95.</summary>
    public double? Map5095 { get; set; }
    /// <summary>Bounding-box loss.</summary>
    public double? BoxLoss { get; set; }
    /// <summary>Classification loss.</summary>
    public double? ClsLoss { get; set; }
    /// <summary>Distribution focal loss.</summary>
    public double? DflLoss { get; set; }
    /// <summary>Validation precision.</summary>
    public double? Precision { get; set; }
    /// <summary>Validation recall.</summary>
    public double? Recall { get; set; }
    /// <summary>Whether any metric has been captured.</summary>
    public bool HasValue => Map50.HasValue || Map5095.HasValue || BoxLoss.HasValue || ClsLoss.HasValue || DflLoss.HasValue;
}
