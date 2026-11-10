namespace Snet.Yolo.Server.anomalib;

using System.Text.Json;
using System.Text.Json.Serialization;

/// <summary>单次 Anomalib 训练请求。</summary>
public sealed class AnomalibTrainingRequest
{
    /// <summary>工程所有者。</summary>
    public required string Owner { get; init; }

    /// <summary>工程标识。</summary>
    public required string ProjectId { get; init; }

    /// <summary>该工程本次训练的独立工作目录。</summary>
    public required string WorkingDirectory { get; init; }

    /// <summary>独立 Anomalib 虚拟环境内的 Python 可执行文件。</summary>
    public required string PythonExecutable { get; init; }

    /// <summary>已经通过上传校验的正常图片路径。</summary>
    public required IReadOnlyList<string> Images { get; init; }

    /// <summary>训练选项。</summary>
    public required AnomalibTrainingOptions Options { get; init; }
}
