namespace Snet.Yolo.Server.anomalib;

using System.Text.Json;
using System.Text.Json.Serialization;

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

    /// <summary>内部正常图阈值标定的详细来源；旧报告可省略。</summary>
    [JsonPropertyName("normalCalibration")]
    public AnomalibNormalCalibrationResult? NormalCalibration { get; init; }

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
        && (NormalCalibration is null || NormalCalibration.IsValid)
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
            if (NormalCalibration is { IsValid: false } normal)
            {
                return normal.Errors.Count > 0 ? string.Join("；", normal.Errors) : "内部正常阈值标定未完成或数据无效。";
            }
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
