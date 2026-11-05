using Snet.Yolo.Server.anomalib;

namespace Snet.Yolo.Api.Model;

/// <summary>HTTP 可序列化的 Anomalib 模型信息，不暴露服务器本地文件路径。</summary>
public sealed class AnomalibModelResponse
{
    /// <summary>模型所属项目的标识。</summary>
    public required string ProjectId { get; init; }
    /// <summary>模型训练或导入运行的标识，用于后续识别、下载与删除。</summary>
    public required string RunId { get; init; }
    /// <summary>模型显示名称。</summary>
    public required string Name { get; init; }
    /// <summary>模型的可选描述。</summary>
    public string? Description { get; init; }
    /// <summary>异常检测算法。</summary>
    public AnomalibModelKind Model { get; init; }
    /// <summary>模型注册的 UTC 时间。</summary>
    public DateTime RegisteredAtUtc { get; init; }
}
