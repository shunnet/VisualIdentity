using System.Text.Json.Serialization;

namespace Snet.Yolo.Server.anomalib;

/// <summary>
/// Anomalib 清单无效时抛出的异常。
/// </summary>
public sealed class AnomalibManifestException : Exception
{
    /// <summary>
    /// 使用错误消息创建清单异常。
    /// </summary>
    /// <param name="message">中文错误消息。</param>
    public AnomalibManifestException(string message) : base(message)
    {
    }

    /// <summary>
    /// 使用错误消息和内部异常创建清单异常。
    /// </summary>
    /// <param name="message">中文错误消息。</param>
    /// <param name="innerException">原始异常。</param>
    public AnomalibManifestException(string message, Exception innerException) : base(message, innerException)
    {
    }
}
