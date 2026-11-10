namespace Snet.Yolo.Server.anomalib;

using System.Text.Json;
using System.Text.Json.Serialization;

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
