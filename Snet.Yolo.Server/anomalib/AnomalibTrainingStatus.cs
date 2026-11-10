namespace Snet.Yolo.Server.anomalib;

using System.Text.Json;
using System.Text.Json.Serialization;

/// <summary>可供页面和持久化层使用的 Anomalib 训练状态快照。</summary>
public sealed class AnomalibTrainingStatus
{
    /// <summary>工程所有者。</summary>
    public string Owner { get; set; } = string.Empty;

    /// <summary>工程标识。</summary>
    public string ProjectId { get; set; } = string.Empty;

    /// <summary>当前训练阶段。</summary>
    public AnomalibTrainingPhase Phase { get; set; } = AnomalibTrainingPhase.Idle;

    /// <summary>当前阶段是否仍在占用训练流程。</summary>
    [JsonIgnore]
    public bool IsActive => Phase is AnomalibTrainingPhase.PreparingDataset or AnomalibTrainingPhase.PreparingEnvironment or AnomalibTrainingPhase.Training or AnomalibTrainingPhase.Exporting or AnomalibTrainingPhase.ValidatingParity;

    /// <summary>供界面显示的流程阶段进度；不是训练轮数完成率。</summary>
    [JsonIgnore]
    public int StagePercent => Phase switch
    {
        AnomalibTrainingPhase.PreparingEnvironment => 10,
        AnomalibTrainingPhase.PreparingDataset => 25,
        AnomalibTrainingPhase.Training => 50,
        AnomalibTrainingPhase.Exporting => 75,
        AnomalibTrainingPhase.ValidatingParity => 90,
        AnomalibTrainingPhase.Complete => 100,
        AnomalibTrainingPhase.CalibrationFailed => 100,
        _ => 0,
    };

    /// <summary>供工程页和训练页共用的中文阶段名称。</summary>
    [JsonIgnore]
    public string PhaseLabel => Phase switch
    {
        AnomalibTrainingPhase.PreparingDataset => "准备数据",
        AnomalibTrainingPhase.PreparingEnvironment => "检查环境",
        AnomalibTrainingPhase.Training => "训练中",
        AnomalibTrainingPhase.Exporting => "导出模型",
        AnomalibTrainingPhase.ValidatingParity => "一致性验证",
        AnomalibTrainingPhase.Complete => "已完成",
        AnomalibTrainingPhase.CalibrationFailed => "校准未通过",
        AnomalibTrainingPhase.Failed => "失败",
        AnomalibTrainingPhase.Cancelled => "已取消",
        _ => "未开始",
    };

    /// <summary>当前模型。</summary>
    public AnomalibModelKind Model { get; set; } = AnomalibModelKind.Padim;

    /// <summary>本次训练选择的设备，供训练页切换回来后恢复概要。</summary>
    public string Device { get; set; } = "auto";

    /// <summary>本次训练使用的模型输入尺寸。</summary>
    public int ImageSize { get; set; } = 640;

    /// <summary>本次训练配置的最大轮数。</summary>
    public int MaxEpochs { get; set; } = 500;

    /// <summary>本次 EfficientAD 训练是否启用早停。</summary>
    public bool EarlyStoppingEnabled { get; set; } = true;

    /// <summary>本次训练允许连续无改善的验证轮数。</summary>
    public int EarlyStoppingPatience { get; set; } = 50;

    /// <summary>本次训练认定验证指标改善所需的最小增量。</summary>
    public double EarlyStoppingMinDelta { get; set; } = 0.001;

    /// <summary>已经完整导出的运行标识，用于当前用户下载模型及校准报告。</summary>
    public string? ExportRunId { get; set; }

    /// <summary>最后一次校准的完整逐图结果；尚未校准时为空。</summary>
    public AnomalibParityResult? Calibration { get; set; }

    /// <summary>当前用户可读消息。</summary>
    public string Message { get; set; } = string.Empty;

    /// <summary>最近一次失败原因。</summary>
    public string? LastError { get; set; }

    /// <summary>去重后的正常训练图片数。</summary>
    public int TrainingImageCount { get; set; }

    /// <summary>稳定划入校准集的正常图片数。</summary>
    public int CalibrationImageCount { get; set; }

    /// <summary>训练数据集内容摘要。</summary>
    public string DatasetSha256 { get; set; } = string.Empty;

    /// <summary>最近更新时间（UTC）。</summary>
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>最近的有界训练输出，供页面显示具体进展和失败原因。</summary>
    public IReadOnlyList<string> LogTail { get; set; } = [];

    /// <summary>训练实时指标：轮次、损失、验证 AUROC、PatchCore 显存规划与阈值，训练页右侧据此展示。</summary>
    public AnomalibLiveMetrics Live { get; set; } = new();

    /// <summary>本次训练开始时间（UTC）；尚未开始时为空。</summary>
    public DateTime? StartedAt { get; set; }

    /// <summary>创建与可变源对象无关的状态快照。</summary>
    /// <returns>状态副本。</returns>
    public AnomalibTrainingStatus Clone() => new()
    {
        Owner = Owner,
        ProjectId = ProjectId,
        Phase = Phase,
        Model = Model,
        Device = Device,
        ImageSize = ImageSize,
        MaxEpochs = MaxEpochs,
        EarlyStoppingEnabled = EarlyStoppingEnabled,
        EarlyStoppingPatience = EarlyStoppingPatience,
        EarlyStoppingMinDelta = EarlyStoppingMinDelta,
        ExportRunId = ExportRunId,
        Calibration = Calibration,
        Message = Message,
        LastError = LastError,
        TrainingImageCount = TrainingImageCount,
        CalibrationImageCount = CalibrationImageCount,
        DatasetSha256 = DatasetSha256,
        UpdatedAt = UpdatedAt,
        LogTail = LogTail.ToArray(),
        Live = Live.Clone(),
        StartedAt = StartedAt,
    };
}
