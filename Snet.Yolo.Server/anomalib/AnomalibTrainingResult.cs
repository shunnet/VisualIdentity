namespace Snet.Yolo.Server.anomalib;

using System.Text.Json;
using System.Text.Json.Serialization;

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
