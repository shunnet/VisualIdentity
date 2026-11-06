using System.Text.Json.Serialization;

namespace Snet.Yolo.Server.anomalib;

/// <summary>训练内部留出的一张正常图片的原始分数，用于追溯阈值来源。</summary>
public sealed class AnomalibNormalCalibrationSample
{
    /// <summary>源图片文件名。</summary>
    [JsonPropertyName("fileName")] public string FileName { get; init; } = string.Empty;
    /// <summary>归一化前的整图异常分数。</summary>
    [JsonPropertyName("rawImageScore")] public double RawImageScore { get; init; }
    /// <summary>归一化前的异常图最小值；早期诊断报告可省略。</summary>
    [JsonPropertyName("rawPixelMinimum")] public double? RawPixelMinimum { get; init; }
    /// <summary>归一化前的异常图最大值。</summary>
    [JsonPropertyName("rawPixelMaximum")] public double RawPixelMaximum { get; init; }
}
