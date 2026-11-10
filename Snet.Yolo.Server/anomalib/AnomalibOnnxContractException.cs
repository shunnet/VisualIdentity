namespace Snet.Yolo.Server.anomalib;

/// <summary>
/// ONNX 输出契约不匹配时抛出的异常。
/// </summary>
public sealed class AnomalibOnnxContractException : Exception
{
    /// <summary>
    /// 使用中文错误消息创建输出契约异常。
    /// </summary>
    /// <param name="message">错误消息。</param>
    public AnomalibOnnxContractException(string message) : base(message)
    {
    }
}
