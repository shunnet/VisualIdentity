namespace Snet.Yolo.Tasks.Core.Training;

using Snet.Yolo.Tasks.Core.History;
using System;
using System.Collections.Generic;

/// <summary>训练状态（可序列化，供 UI 与首页卡片使用）。</summary>
public sealed class TrainingStatus
{
    /// <summary>实时曲线最多保留的点数；超过后丢弃最早的点，避免超长训练让状态与推送越来越大。</summary>
    public const int MaximumCurvePoints = 1000;

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
    /// <summary>每个轮次一个点的验证曲线（轮次、mAP50-95、训练损失之和），供训练页实时折线图使用；最多 <see cref="MaximumCurvePoints"/> 个点。</summary>
    public List<TrainingCurvePoint> Curve { get; set; } = new();
    /// <summary>验证分数（mAP50-95，没有时取 mAP50）最高的轮次；还没有记录过任何轮次时为 0。分数相同时保留最早达到的轮次。</summary>
    public int BestEpoch { get; set; }
    /// <summary><see cref="BestEpoch"/> 对应的验证分数；还没有最佳轮次时为空。</summary>
    public double? BestScore { get; set; }
    /// <summary>Last update timestamp in UTC.</summary>
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// 把当前轮次的最新指标记为曲线上的一个点：分数取 mAP50-95（没有时取 mAP50），损失取框、分类、分布焦点三项之和。
    /// 同一轮次再次出现（例如训练结束后对 best.pt 的最终验证）时覆盖该点，保证每个轮次只有一个点；还没有轮次或没有 mAP 时不记录。
    /// 最佳轮次只在一个轮次第一次出现时参与比较：最终验证的数字属于 best.pt 而不是最后一个轮次，不能把最佳轮次挪到它上面；
    /// 它也独立于曲线的点数上限，曲线丢弃最早的点之后最佳轮次不会变。
    /// </summary>
    public void RecordCurvePoint()
    {
        var score = Metrics.Map5095 ?? Metrics.Map50;
        if (Epoch <= 0 || score is not { } value) { return; }
        double? loss = null;
        foreach (var part in new[] { Metrics.BoxLoss, Metrics.ClsLoss, Metrics.DflLoss })
        {
            if (part is { } partValue) { loss = (loss ?? 0) + partValue; }
        }
        var point = new TrainingCurvePoint { Epoch = Epoch, Score = value, Loss = loss };
        if (Curve.Count > 0 && Curve[^1].Epoch == Epoch) { Curve[^1] = point; }
        else
        {
            Curve.Add(point);
            if (BestScore is not { } best || value > best) { BestScore = value; BestEpoch = Epoch; }
        }
        if (Curve.Count > MaximumCurvePoints) { Curve.RemoveRange(0, Curve.Count - MaximumCurvePoints); }
    }

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
        Curve = new List<TrainingCurvePoint>(Curve),
        BestEpoch = BestEpoch,
        BestScore = BestScore,
        UpdatedAt = UpdatedAt,
    };
}
