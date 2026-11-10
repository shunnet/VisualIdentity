namespace Snet.Yolo.Server.anomalib;

using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;

/// <summary>可按页面当前语言呈现的 Anomalib 模型包校验错误。</summary>
public sealed class AnomalibModelImportException(string resourceKey, Exception? innerException = null)
    : Exception(resourceKey, innerException)
{
    /// <summary>错误说明对应的双语资源键。</summary>
    public string ResourceKey { get; } = resourceKey;
}
