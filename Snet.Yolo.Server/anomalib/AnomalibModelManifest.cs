using System.Text.Json.Serialization;

namespace Snet.Yolo.Server.anomalib;

/// <summary>
/// Anomalib 模型部署清单。
/// </summary>
public sealed class AnomalibModelManifest
{
    /// <summary>获取或设置清单架构版本。</summary>
    [JsonRequired]
    public string SchemaVersion { get; set; } = string.Empty;

    /// <summary>获取或设置模型家族，固定为 anomalib。</summary>
    [JsonRequired]
    public string Family { get; set; } = string.Empty;

    /// <summary>获取或设置生成模型所用的 Anomalib 版本。</summary>
    [JsonRequired]
    public string AnomalibVersion { get; set; } = string.Empty;

    /// <summary>获取或设置异常检测算法。</summary>
    [JsonRequired]
    public AnomalibAlgorithm Algorithm { get; set; }

    /// <summary>获取或设置 ONNX 模型文件的 SHA-256 摘要。</summary>
    [JsonRequired]
    public string ModelSha256 { get; set; } = string.Empty;

    /// <summary>获取或设置模型输入契约。</summary>
    [JsonRequired]
    public AnomalibInputContract Input { get; set; } = new();

    /// <summary>获取或设置模型输出节点契约。</summary>
    [JsonRequired]
    public AnomalibOutputNames Outputs { get; set; } = new();

    /// <summary>获取或设置后处理契约。</summary>
    [JsonRequired]
    public AnomalibPostProcessingContract PostProcessing { get; set; } = new();
}
