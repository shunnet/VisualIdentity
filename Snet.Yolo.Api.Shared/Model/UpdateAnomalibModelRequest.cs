namespace Snet.Yolo.Api.Model;

/// <summary>可编辑的 Anomalib 模型展示信息。</summary>
public sealed class UpdateAnomalibModelRequest
{
    /// <summary>新模型名称。</summary>
    public string Name { get; set; } = string.Empty;
    /// <summary>可选的模型描述。</summary>
    public string? Description { get; set; }
}
