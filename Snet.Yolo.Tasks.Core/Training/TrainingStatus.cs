namespace Snet.Yolo.Tasks.Core.Training;

using System;
using System.Collections.Generic;

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
