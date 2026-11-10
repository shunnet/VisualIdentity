using System.Text.Json.Serialization;

namespace Snet.Yolo.Server.anomalib;

/// <summary>只基于训练内部留出正常图的阈值标定，独立于最终校准验收。</summary>
public sealed class AnomalibNormalCalibrationResult
{
    /// <summary>是否至少完成一张正常图片并更新阈值。</summary>
    [JsonPropertyName("completed")] public bool Completed { get; init; }
    /// <summary>成功标定的正常图片数量。</summary>
    [JsonPropertyName("sampleCount")] public int SampleCount { get; init; }
    /// <summary>阈值覆盖的内部正常图比例；当前规则取“最高分 × 1.05 与均值 + 3.5σ 的较大者”，因此恒为 1。</summary>
    [JsonPropertyName("quantile")] public double Quantile { get; init; }
    /// <summary>阈值规则的文字说明；早期报告省略。</summary>
    [JsonPropertyName("thresholdPolicy")] public string? ThresholdPolicy { get; init; }
    /// <summary>内部正常图整图原始分数的均值；早期报告省略。</summary>
    [JsonPropertyName("imageScoreMean")] public double? ImageScoreMean { get; init; }
    /// <summary>内部正常图整图原始分数的样本标准差；早期报告省略。</summary>
    [JsonPropertyName("imageScoreStd")] public double? ImageScoreStd { get; init; }
    /// <summary>内部正常图整图原始分数的最大值；早期报告省略。</summary>
    [JsonPropertyName("imageScoreMax")] public double? ImageScoreMax { get; init; }
    /// <summary>标定前的整图原始阈值。</summary>
    [JsonPropertyName("previousImageThreshold")] public double? PreviousImageThreshold { get; init; }
    /// <summary>标定前的像素原始阈值。</summary>
    [JsonPropertyName("previousPixelThreshold")] public double? PreviousPixelThreshold { get; init; }
    /// <summary>标定后的整图原始阈值。</summary>
    [JsonPropertyName("imageThreshold")] public double? ImageThreshold { get; init; }
    /// <summary>标定后的像素原始阈值。</summary>
    [JsonPropertyName("pixelThreshold")] public double? PixelThreshold { get; init; }
    /// <summary>参与阈值标定的原始正常分数。</summary>
    [JsonPropertyName("samples")] public IReadOnlyList<AnomalibNormalCalibrationSample> Samples { get; init; } = [];
    /// <summary>内部正常图片的读取或推理错误，存在错误时禁止自动注册。</summary>
    [JsonPropertyName("errors")] public IReadOnlyList<string> Errors { get; init; } = [];

    /// <summary>标定元数据是否完整且无错误；不代表最终独立校准或真实缺陷检出率合格。</summary>
    [JsonIgnore]
    public bool IsValid => Completed && SampleCount > 0 && Samples.Count == SampleCount && Errors.Count == 0
        && Quantile is > 0 and <= 1
        && ImageThreshold is { } image && double.IsFinite(image)
        && PixelThreshold is { } pixel && double.IsFinite(pixel)
        && Samples.All(static sample => double.IsFinite(sample.RawImageScore) && double.IsFinite(sample.RawPixelMaximum)
            && (sample.RawPixelMinimum is null || double.IsFinite(sample.RawPixelMinimum.Value)));
}
