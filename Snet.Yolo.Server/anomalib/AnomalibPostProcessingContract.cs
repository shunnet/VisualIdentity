using System.Text.Json.Serialization;

namespace Snet.Yolo.Server.anomalib;

/// <summary>
/// Anomalib 后处理契约。
/// </summary>
public sealed class AnomalibPostProcessingContract
{
    /// <summary>获取或设置判断异常的阈值。</summary>
    [JsonRequired]
    public float Threshold { get; set; }

    /// <summary>获取或设置异常阈值来源。</summary>
    [JsonRequired]
    public AnomalibThresholdSource ThresholdSource { get; set; }
}
