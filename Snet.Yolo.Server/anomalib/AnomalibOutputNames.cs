using System.Text.Json.Serialization;

namespace Snet.Yolo.Server.anomalib;

/// <summary>
/// Anomalib ONNX 输出节点名称。
/// </summary>
public sealed class AnomalibOutputNames
{
    /// <summary>获取或设置整图异常分数输出名称。</summary>
    [JsonRequired]
    public string PredictionScore { get; set; } = string.Empty;

    /// <summary>获取或设置整图异常标签输出名称。</summary>
    [JsonRequired]
    public string PredictionLabel { get; set; } = string.Empty;

    /// <summary>获取或设置像素级异常图输出名称。</summary>
    [JsonRequired]
    public string AnomalyMap { get; set; } = string.Empty;

    /// <summary>获取或设置像素级二值掩码输出名称。</summary>
    [JsonRequired]
    public string PredictionMask { get; set; } = string.Empty;
}
