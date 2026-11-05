namespace Snet.Yolo.Server.inference;

/// <summary>使用指定 CUDA GPU 或可选 TensorRT 加速的 YOLO 执行提供程序。</summary>
public sealed class CudaExecutionProvider : OnnxExecutionProvider
{
    /// <summary>从文件创建 CUDA/TensorRT 会话。</summary>
    /// <param name="model">ONNX 模型绝对路径。</param>
    /// <param name="gpuId">GPU 编号；-1 保留为显式 CPU 路径。</param>
    /// <param name="trtConfig">可选 TensorRT 引擎配置；空值使用 CUDA。</param>
    public CudaExecutionProvider(string model, int gpuId = 0, TensorRt? trtConfig = null)
        : base(model, OnnxSessionOptions.CreateCuda(gpuId, trtConfig)) { }

    /// <summary>从内存创建 CUDA/TensorRT 会话。</summary>
    /// <param name="model">完整的 ONNX 模型字节。</param>
    /// <param name="gpuId">GPU 编号；-1 保留为显式 CPU 路径。</param>
    /// <param name="trtConfig">可选 TensorRT 引擎配置；空值使用 CUDA。</param>
    public CudaExecutionProvider(byte[] model, int gpuId = 0, TensorRt? trtConfig = null)
        : base(model, OnnxSessionOptions.CreateCuda(gpuId, trtConfig)) { }
}
