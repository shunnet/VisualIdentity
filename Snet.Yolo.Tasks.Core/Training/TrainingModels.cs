namespace Snet.Yolo.Tasks.Core.Training;

using System;
using System.Collections.Generic;

/// <summary>训练生命周期阶段。</summary>
public enum TrainingPhase
{
    Idle,
    Preparing,        // 导出数据集
    EnvironmentCheck, // 检测训练环境
    Installing,       // 搭建训练环境
    Training,         // 训练中
    Validating,       // 验证模型
    Complete,
    Failed,
    Cancelled,
}

/// <summary>训练指标（来自 yolo 日志）。</summary>
public sealed class TrainingMetrics
{
    public double? Map50 { get; set; }
    public double? Map5095 { get; set; }
    public double? BoxLoss { get; set; }
    public double? ClsLoss { get; set; }
    public double? DflLoss { get; set; }
    public double? Precision { get; set; }
    public double? Recall { get; set; }
    public bool HasValue => Map50.HasValue || Map5095.HasValue || BoxLoss.HasValue || ClsLoss.HasValue || DflLoss.HasValue;
}

/// <summary>训练选项（弹窗填写）。</summary>
public sealed class TrainingOptions
{
    /// <summary>训练轮数。默认 300：图片少时 50 轮只有几十次参数更新，模型学不到东西（识别不到任何目标）。</summary>
    public int Epochs { get; set; } = 300;
    public int ImgSize { get; set; } = 640;
    /// <summary>auto / cpu / 0 / 0,1</summary>
    public string Device { get; set; } = "auto";
    public string Model { get; set; } = "yolo26n.pt";
    public string Task { get; set; } = "detect";
    /// <summary>是否使用验证集（自动划分训练集的 10%）。默认关闭：由用户按需勾选，避免小数据集白白少掉 10% 训练图。</summary>
    public bool UseVal { get; set; }
}

/// <summary>训练状态（可序列化，供 UI 与首页卡片使用）。</summary>
public sealed class TrainingStatus
{
    public string Owner { get; set; } = "snet";
    public string ProjectId { get; set; } = string.Empty;
    public TrainingPhase Phase { get; set; } = TrainingPhase.Idle;
    public string Device { get; set; } = string.Empty;
    public string GpuName { get; set; } = string.Empty;
    public string YoloVersion { get; set; } = string.Empty;
    public string ModelName { get; set; } = "yolo26n.pt";
    public string BestModelPath { get; set; } = string.Empty;
    public int Epoch { get; set; }
    public int TotalEpochs { get; set; }
    /// <summary>0-100</summary>
    public int Percent { get; set; }
    public string Message { get; set; } = string.Empty;
    public string? LastError { get; set; }
    public TrainingMetrics Metrics { get; set; } = new();
    public List<string> LogTail { get; set; } = new();
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public bool IsActive => Phase is TrainingPhase.Preparing or TrainingPhase.EnvironmentCheck
        or TrainingPhase.Installing or TrainingPhase.Training or TrainingPhase.Validating;

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
