namespace Snet.Yolo.Server.anomalib;

using System.Text.Json;
using System.Text.Json.Serialization;

/// <summary>第一阶段支持的 Anomalib 训练模型。</summary>
public enum AnomalibModelKind
{
    /// <summary>稳定优先的 PaDiM 模型。</summary>
    Padim,

    /// <summary>速度优先的 EfficientAD Small 模型。</summary>
    EfficientAdSmall,

    /// <summary>基于正常图像特征库的 PatchCore 模型。</summary>
    PatchCore,

    /// <summary>兼容旧接口和已保存的 PatchCore 实验类型。</summary>
    PatchcoreExperimental = PatchCore,
}

/// <summary>Anomalib 训练生命周期阶段。</summary>
public enum AnomalibTrainingPhase
{
    /// <summary>尚未开始训练。</summary>
    Idle,

    /// <summary>正在生成仅含正常图片的数据集。</summary>
    PreparingDataset,

    /// <summary>正在检查或安装独立 Python 环境。</summary>
    PreparingEnvironment,

    /// <summary>正在训练异常检测模型。</summary>
    Training,

    /// <summary>正在导出 ONNX 模型。</summary>
    Exporting,

    /// <summary>正在执行本次训练模型与 ONNX 一致性门禁。</summary>
    ValidatingParity,

    /// <summary>模型已完成并通过一致性门禁。</summary>
    Complete,

    /// <summary>训练或一致性验证失败。</summary>
    Failed,

    /// <summary>用户取消了训练。</summary>
    Cancelled,

    /// <summary>模型已完整导出，但校准未通过；保留下载，等待用户自行验证。</summary>
    CalibrationFailed,
}

/// <summary>Anomalib 模型的稳定描述。</summary>
/// <param name="Kind">应用侧模型类型。</param>
/// <param name="PythonName">传给 Python 流水线的稳定名称。</param>
/// <param name="DisplayName">界面显示名称。</param>
/// <param name="Experimental">是否属于实验模型。</param>
public sealed record AnomalibModelDescriptor(
    AnomalibModelKind Kind,
    string PythonName,
    string DisplayName,
    bool Experimental);

/// <summary>受支持模型的集中映射，避免页面、服务与 Python 脚本各自维护名称。</summary>
public static class AnomalibModelCatalog
{
    /// <summary>获取指定模型的不可变描述。</summary>
    /// <param name="kind">模型类型。</param>
    /// <returns>模型描述。</returns>
    public static AnomalibModelDescriptor Get(AnomalibModelKind kind) => kind switch
    {
        AnomalibModelKind.Padim => new(kind, "padim", "PaDiM", false),
        AnomalibModelKind.EfficientAdSmall => new(kind, "efficient_ad_small", "EfficientAD Small", false),
        AnomalibModelKind.PatchCore => new(AnomalibModelKind.PatchCore, "patchcore", "PatchCore", false),
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "不支持的 Anomalib 模型。"),
    };
}

/// <summary>Anomalib 训练选项。</summary>
public sealed class AnomalibTrainingOptions
{
    /// <summary>校准集正常图片允许的最高误报比例。</summary>
    public const double MaximumNormalFalsePositiveRate = 0.05;

    /// <summary>要训练的异常检测模型。</summary>
    public AnomalibModelKind Model { get; set; } = AnomalibModelKind.Padim;

    /// <summary>正方形模型输入尺寸，必须在 128 至 2048 之间且为 32 的倍数。</summary>
    public int ImageSize { get; set; } = 640;

    /// <summary>迭代训练模型的最大轮数；PaDiM 与 PatchCore 会由 Anomalib 自动收敛为所需轮数。</summary>
    public int MaxEpochs { get; set; } = 500;

    /// <summary>EfficientAD 是否在验证指标持续无改善时提前结束；其他模型忽略此设置。</summary>
    public bool EarlyStoppingEnabled { get; set; } = true;

    /// <summary>EfficientAD 连续无明显改善的验证轮数，范围 1 至 1000。</summary>
    public int EarlyStoppingPatience { get; set; } = 50;

    /// <summary>像素级 AUROC 认定改善所需的最小增量，范围 0 至 1。</summary>
    public double EarlyStoppingMinDelta { get; set; } = 0.001;

    /// <summary>设备选择：auto、cpu 或 cuda。</summary>
    public string Device { get; set; } = "auto";

    /// <summary>从正常图片中稳定划入校准集的比例。</summary>
    public double CalibrationRatio { get; set; } = 0.1;

    /// <summary>保证数据划分和训练可复现的随机种子。</summary>
    public int RandomSeed { get; set; } = 42;

    /// <summary>训练数据加载进程数；零表示由 Python 在当前平台安全选择。</summary>
    public int WorkerCount { get; set; }

    /// <summary>验证训练选项并在无效时抛出可执行的中文异常。</summary>
    public void Validate()
    {
        _ = AnomalibModelCatalog.Get(Model);
        if (ImageSize is < 128 or > 2048 || ImageSize % 32 != 0)
        {
            throw new ArgumentOutOfRangeException(nameof(ImageSize), "输入尺寸必须在 128 至 2048 之间且为 32 的倍数。");
        }
        if (CalibrationRatio is < 0.05 or > 0.5)
        {
            throw new ArgumentOutOfRangeException(nameof(CalibrationRatio), "正常图片校准比例必须在 5% 至 50% 之间。");
        }
        if (MaxEpochs is < 1 or > 10_000)
        {
            throw new ArgumentOutOfRangeException(nameof(MaxEpochs), "训练轮数必须在 1 至 10000 之间。");
        }
        if (WorkerCount is < 0 or > 64)
        {
            throw new ArgumentOutOfRangeException(nameof(WorkerCount), "数据加载进程数必须在 0 至 64 之间。");
        }
        if (!double.IsFinite(EarlyStoppingMinDelta))
        {
            throw new ArgumentOutOfRangeException(nameof(EarlyStoppingMinDelta), "早停最小改善幅度必须是有限值。");
        }
        if (Model == AnomalibModelKind.EfficientAdSmall && EarlyStoppingEnabled)
        {
            if (EarlyStoppingPatience is < 1 or > 1000)
            {
                throw new ArgumentOutOfRangeException(nameof(EarlyStoppingPatience), "早停耐心轮数必须在 1 至 1000 之间。");
            }
            if (EarlyStoppingMinDelta is < 0 or > 1)
            {
                throw new ArgumentOutOfRangeException(nameof(EarlyStoppingMinDelta), "早停最小改善幅度必须是 0 至 1 之间的有限值。");
            }
        }
        var selectedGpus = Snet.Yolo.Server.models.GpuDeviceSelection.Parse(Device, anomalib: true);
        if (Model != AnomalibModelKind.EfficientAdSmall && selectedGpus.Length > 1)
        {
            throw new ArgumentException("当前模型仅支持单张 GPU 训练。", nameof(Device));
        }
    }
}

/// <summary>一张已经通过上传校验的正常图片。</summary>
/// <param name="SourcePath">源图片绝对路径。</param>
/// <param name="ContentSha256">图片内容的 SHA-256 小写十六进制摘要。</param>
public sealed record AnomalibImageSource(string SourcePath, string ContentSha256);

/// <summary>正常图片的稳定训练集与校准集划分。</summary>
/// <param name="TrainingImages">训练图片。</param>
/// <param name="CalibrationImages">校准与一致性门禁图片。</param>
/// <param name="DatasetSha256">由有序内容摘要计算的数据集摘要。</param>
public sealed record AnomalibDatasetSplit(
    IReadOnlyList<AnomalibImageSource> TrainingImages,
    IReadOnlyList<AnomalibImageSource> CalibrationImages,
    string DatasetSha256)
{
    /// <summary>去重后的正常图片总数。</summary>
    public int TotalUniqueImages => TrainingImages.Count + CalibrationImages.Count;
}

/// <summary>单次 Anomalib 训练请求。</summary>
public sealed class AnomalibTrainingRequest
{
    /// <summary>工程所有者。</summary>
    public required string Owner { get; init; }

    /// <summary>工程标识。</summary>
    public required string ProjectId { get; init; }

    /// <summary>该工程本次训练的独立工作目录。</summary>
    public required string WorkingDirectory { get; init; }

    /// <summary>独立 Anomalib 虚拟环境内的 Python 可执行文件。</summary>
    public required string PythonExecutable { get; init; }

    /// <summary>已经通过上传校验的正常图片路径。</summary>
    public required IReadOnlyList<string> Images { get; init; }

    /// <summary>训练选项。</summary>
    public required AnomalibTrainingOptions Options { get; init; }
}

/// <summary>Python 流水线进程结果。</summary>
/// <param name="ExitCode">进程退出码。</param>
/// <param name="OutputTail">用于失败诊断的有界输出尾部。</param>
public sealed record AnomalibProcessResult(int ExitCode, string OutputTail);

/// <summary>已完成导出的训练产物；校准通过后自动注册，未通过时仅供手动下载。</summary>
public sealed class AnomalibTrainingArtifact
{
    /// <summary>工程所有者。</summary>
    public required string Owner { get; init; }

    /// <summary>工程标识。</summary>
    public required string ProjectId { get; init; }

    /// <summary>训练模型类型。</summary>
    public required AnomalibModelKind Model { get; init; }

    /// <summary>可信训练过程产生的 checkpoint 路径。</summary>
    public required string CheckpointPath { get; init; }

    /// <summary>已成功导出的 ONNX 路径，校准状态由 Parity 单独记录。</summary>
    public required string OnnxPath { get; init; }

    /// <summary>描述预处理、输出契约与阈值的清单路径。</summary>
    public required string ManifestPath { get; init; }

    /// <summary>训练数据集内容摘要。</summary>
    public required string DatasetSha256 { get; init; }

    /// <summary>一致性验证结果。</summary>
    public required AnomalibParityResult Parity { get; init; }
}

/// <summary>Anomalib 训练服务的最终结果。</summary>
public sealed class AnomalibTrainingResult
{
    /// <summary>训练、导出、一致性验证和注册是否全部成功。</summary>
    public required bool Succeeded { get; init; }

    /// <summary>最终用户可读消息。</summary>
    public required string Message { get; init; }

    /// <summary>一致性验证结果。</summary>
    public required AnomalibParityResult Parity { get; init; }

    /// <summary>完整导出后产生的模型产物；未通过校准时仍可存在。</summary>
    public AnomalibTrainingArtifact? Artifact { get; init; }
}

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
    };
}

/// <summary>本次训练模型与 ONNX 一致性门禁结果。</summary>
public sealed class AnomalibParityResult
{
    /// <summary>Python 报告的状态：passed 或 failed。</summary>
    [JsonPropertyName("status")]
    public string Status { get; init; } = "failed";

    /// <summary>ONNX 与清单已成功生成并可被运行时加载；不代表校准通过。</summary>
    [JsonPropertyName("exportSucceeded")]
    public bool ExportSucceeded { get; init; }

    /// <summary>计划处理的全部校准图片数，包含推理失败的图片。</summary>
    [JsonPropertyName("totalSampleCount")]
    public int TotalSampleCount { get; init; }

    /// <summary>每张校准图片的完整诊断，按实际处理顺序排列。</summary>
    [JsonPropertyName("sampleResults")]
    public IReadOnlyList<AnomalibCalibrationSampleResult> SampleResults { get; init; } = Array.Empty<AnomalibCalibrationSampleResult>();

    /// <summary>同时通过正常判定和一致性规则的图片数。</summary>
    [JsonIgnore]
    public int PassedSampleCount => SampleResults.Count(static sample => sample.Passed);

    /// <summary>未通过或未完成双路比较的图片数。</summary>
    [JsonIgnore]
    public int FailedSampleCount => Math.Max(0, TotalSampleCount - PassedSampleCount);

    /// <summary>逐图通过率，取值为 0 至 1；没有样本时为 0。</summary>
    [JsonIgnore]
    public double PassRate => TotalSampleCount > 0 ? (double)PassedSampleCount / TotalSampleCount : 0;

    /// <summary>成功完成双路推理的正常图误报率，取值为 0 至 1。</summary>
    [JsonIgnore]
    public double NormalFalsePositiveRate => NormalSampleCount > 0 ? (double)NormalFalsePositiveCount / NormalSampleCount : 0;

    /// <summary>实际完成双路推理比较的样本数。</summary>
    [JsonPropertyName("sampleCount")]
    public int SampleCount { get; init; }

    /// <summary>完成正常图片误报检查的样本数。</summary>
    [JsonPropertyName("normalSampleCount")]
    public int NormalSampleCount { get; init; }

    /// <summary>被 ONNX 错判为异常的正常图片数。</summary>
    [JsonPropertyName("normalFalsePositiveCount")]
    public int NormalFalsePositiveCount { get; init; }

    /// <summary>所有样本的最大图像异常分数绝对差。</summary>
    [JsonPropertyName("maxScoreDifference")]
    public double MaxScoreDifference { get; init; } = double.PositiveInfinity;

    /// <summary>所有样本中的最小二值掩码 IoU。</summary>
    [JsonPropertyName("minimumMaskIou")]
    public double MinimumMaskIou { get; init; }

    /// <summary>checkpoint 和 ONNX 标签不一致的样本数。</summary>
    [JsonPropertyName("labelMismatches")]
    public int LabelMismatches { get; init; }

    /// <summary>Python 记录的错误列表。</summary>
    [JsonPropertyName("errors")]
    public IReadOnlyList<string> Errors { get; init; } = Array.Empty<string>();

    /// <summary>C# 二次校验后的真实通过状态。</summary>
    [JsonIgnore]
    public bool Passed => string.Equals(Status, "passed", StringComparison.OrdinalIgnoreCase)
        && SampleCount > 0
        && (TotalSampleCount == 0 || (TotalSampleCount == SampleCount && SampleResults.Count == TotalSampleCount))
        && NormalSampleCount == SampleCount
        && NormalFalsePositiveCount >= 0
        && NormalFalsePositiveCount <= NormalSampleCount * AnomalibTrainingOptions.MaximumNormalFalsePositiveRate
        && LabelMismatches == 0
        && Errors.Count == 0
        && double.IsFinite(MaxScoreDifference)
        && MaxScoreDifference <= 0.02
        && MinimumMaskIou >= 0.95;

    /// <summary>该结果是否允许进入模型注册流程。</summary>
    [JsonIgnore]
    public bool CanRegister => Passed;

    /// <summary>门禁未通过时的中文原因；通过时为空字符串。</summary>
    [JsonIgnore]
    public string FailureReason
    {
        get
        {
            if (Passed) { return string.Empty; }
            if (Errors.Count > 0) { return string.Join("；", Errors); }
            if (SampleCount <= 0) { return "没有完成任何一致性样本比较。"; }
            if (NormalSampleCount != SampleCount || NormalFalsePositiveCount < 0) { return "正常图片误报统计缺失或无效。"; }
            if (NormalFalsePositiveCount > NormalSampleCount * AnomalibTrainingOptions.MaximumNormalFalsePositiveRate)
            {
                return $"正常图片误报 {NormalFalsePositiveCount}/{NormalSampleCount}，超过允许的 5%。";
            }
            if (LabelMismatches > 0) { return $"训练模型与 ONNX 有 {LabelMismatches} 个标签不一致。"; }
            if (!double.IsFinite(MaxScoreDifference) || MaxScoreDifference > 0.02) { return $"异常分数最大差值 {MaxScoreDifference:0.######} 超过 0.02。"; }
            if (MinimumMaskIou < 0.95) { return $"掩码最小 IoU {MinimumMaskIou:0.######} 低于 0.95。"; }
            return "一致性脚本未报告通过。";
        }
    }

    /// <summary>严格解析 Python 写出的 JSON，并拒绝缺失或非法结果。</summary>
    /// <param name="json">一致性结果 JSON。</param>
    /// <returns>经过结构解析的门禁结果。</returns>
    public static AnomalibParityResult Parse(string json)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(json);
        try
        {
            return JsonSerializer.Deserialize<AnomalibParityResult>(json, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = false,
                UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
            }) ?? throw new InvalidDataException("一致性结果为空。");
        }
        catch (JsonException error)
        {
            throw new InvalidDataException("无法解析 Anomalib 一致性结果。", error);
        }
    }

    /// <summary>创建尚未执行门禁时使用的失败结果。</summary>
    /// <param name="reason">失败原因。</param>
    /// <returns>禁止注册的失败结果。</returns>
    public static AnomalibParityResult NotRun(string reason) => new() { Errors = [reason] };
}
