namespace Snet.Yolo.Tasks.Core.Training;

using System;
using System.Collections.Generic;

/// <summary>训练生命周期阶段。</summary>
public enum TrainingPhase
{
    /// <summary>No training is running.</summary>
    Idle,
    /// <summary>Dataset export is running.</summary>
    Preparing,        // 导出数据集
    /// <summary>The Python and accelerator environment is being checked.</summary>
    EnvironmentCheck, // 检测训练环境
    /// <summary>Training dependencies are being installed.</summary>
    Installing,       // 搭建训练环境
    /// <summary>Ultralytics training is running.</summary>
    Training,         // 训练中
    /// <summary>The trained model is being validated.</summary>
    Validating,       // 验证模型
    /// <summary>Training completed successfully.</summary>
    Complete,
    /// <summary>Training failed.</summary>
    Failed,
    /// <summary>Training was cancelled.</summary>
    Cancelled,
}

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

/// <summary>训练选项（弹窗填写）。</summary>
public sealed class TrainingOptions
{
    /// <summary>训练轮数。默认 300：图片少时 50 轮只有几十次参数更新，模型学不到东西（识别不到任何目标）。</summary>
    public int Epochs { get; set; } = 300;
    /// <summary>Square training image size.</summary>
    public int ImgSize { get; set; } = 640;
    /// <summary>auto / cpu / 0 / 0,1</summary>
    public string Device { get; set; } = "auto";
    /// <summary>Base Ultralytics model name or path.</summary>
    public string Model { get; set; } = "yolo26n.pt";
    /// <summary>Ultralytics task name.</summary>
    public string Task { get; set; } = "detect";
    /// <summary>是否使用验证集（自动划分训练集的 10%）。默认关闭：由用户按需勾选，避免小数据集白白少掉 10% 训练图。</summary>
    public bool UseVal { get; set; }
}

/// <summary>训练状态（可序列化，供 UI 与首页卡片使用）。</summary>
public sealed class TrainingStatus
{
    /// <summary>Owning username.</summary>
    public string Owner { get; set; } = "snet";
    /// <summary>Workspace project identifier.</summary>
    public string ProjectId { get; set; } = string.Empty;
    /// <summary>Current lifecycle phase.</summary>
    public TrainingPhase Phase { get; set; } = TrainingPhase.Idle;
    /// <summary>Selected Ultralytics device.</summary>
    public string Device { get; set; } = string.Empty;
    /// <summary>Detected accelerator name.</summary>
    public string GpuName { get; set; } = string.Empty;
    /// <summary>Detected Ultralytics version.</summary>
    public string YoloVersion { get; set; } = string.Empty;
    /// <summary>Selected model name.</summary>
    public string ModelName { get; set; } = "yolo26n.pt";
    /// <summary>Absolute best-model output path.</summary>
    public string BestModelPath { get; set; } = string.Empty;
    /// <summary>Current epoch.</summary>
    public int Epoch { get; set; }
    /// <summary>Requested epoch count.</summary>
    public int TotalEpochs { get; set; }
    /// <summary>0-100</summary>
    public int Percent { get; set; }
    /// <summary>Current user-facing status message.</summary>
    public string Message { get; set; } = string.Empty;
    /// <summary>Last failure message, if any.</summary>
    public string? LastError { get; set; }
    /// <summary>Latest parsed metrics.</summary>
    public TrainingMetrics Metrics { get; set; } = new();
    /// <summary>Bounded recent log lines.</summary>
    public List<string> LogTail { get; set; } = new();
    /// <summary>Last update timestamp in UTC.</summary>
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>Whether work is currently in progress.</summary>
    public bool IsActive => Phase is TrainingPhase.Preparing or TrainingPhase.EnvironmentCheck
        or TrainingPhase.Installing or TrainingPhase.Training or TrainingPhase.Validating;

    /// <summary>Creates a detached status snapshot.</summary>
    public TrainingStatus Clone(bool includeLogs = true) => new()
    {
        Owner = Owner,
        ProjectId = ProjectId,
        Phase = Phase,
        Device = Device,
        BestModelPath = BestModelPath,
        GpuName = GpuName,
        YoloVersion = YoloVersion,
        ModelName = ModelName,
        Epoch = Epoch,
        TotalEpochs = TotalEpochs,
        Percent = Percent,
        Message = Message,
        LastError = LastError,
        Metrics = new TrainingMetrics { Map50 = Metrics.Map50, Map5095 = Metrics.Map5095, BoxLoss = Metrics.BoxLoss, ClsLoss = Metrics.ClsLoss, DflLoss = Metrics.DflLoss, Precision = Metrics.Precision, Recall = Metrics.Recall },
        LogTail = includeLogs ? new List<string>(LogTail) : new List<string>(),
        UpdatedAt = UpdatedAt,
    };
}
