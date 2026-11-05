namespace Snet.Yolo.Server.inference;

/// <summary>使用当前宿主原生运行库中的 CPU 执行路径。</summary>
public sealed class CpuExecutionProvider : OnnxExecutionProvider
{
    /// <summary>从文件创建 CPU 推理会话。</summary>
    /// <param name="model">ONNX 模型绝对路径。</param>
    public CpuExecutionProvider(string model) : base(model, OnnxSessionOptions.CreateCpu()) { }

    /// <summary>从内存创建 CPU 推理会话。</summary>
    /// <param name="model">完整的 ONNX 模型字节。</param>
    public CpuExecutionProvider(byte[] model) : base(model, OnnxSessionOptions.CreateCpu()) { }
}
