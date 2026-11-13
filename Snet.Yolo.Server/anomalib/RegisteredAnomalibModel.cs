namespace Snet.Yolo.Server.anomalib;

using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;

/// <summary>经训练门禁注册或从模型包导入、可供独立验证页加载的 Anomalib 模型。</summary>
public sealed class RegisteredAnomalibModel
{
    /// <summary>验证页显示的模型名称。</summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>用户填写的模型描述。</summary>
    public string Description { get; init; } = string.Empty;

    /// <summary>注册标记是否包含用户填写的名称。</summary>
    public bool HasCustomName { get; init; }

    /// <summary>是否引用原始训练产物；删除验证注册时必须保留训练文件。</summary>
    public bool IsTrainingModel { get; init; }

    /// <summary>训练清单完成写入的 UTC 时间，重复添加到验证页时保持稳定。</summary>
    public DateTime? TrainingCompletedAtUtc { get; init; }

    /// <summary>模型所在工程的标识。</summary>
    public required string ProjectId { get; init; }

    /// <summary>一次训练运行的标识。</summary>
    public required string RunId { get; init; }

    /// <summary>训练模型类型。</summary>
    public required AnomalibModelKind Model { get; init; }

    /// <summary>ONNX 模型的绝对路径。</summary>
    public required string OnnxPath { get; init; }

    /// <summary>模型清单的绝对路径。</summary>
    public required string ManifestPath { get; init; }

    /// <summary>完成注册的 UTC 时间。</summary>
    public required DateTime RegisteredAtUtc { get; init; }
}
