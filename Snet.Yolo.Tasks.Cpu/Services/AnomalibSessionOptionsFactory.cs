using Microsoft.ML.OnnxRuntime;
using OrtSessionOptions = Microsoft.ML.OnnxRuntime.SessionOptions;

namespace Snet.Yolo.Tasks.Services;

/// <summary>为 CPU 发行包创建经过图优化的 Anomalib ONNX 会话选项。</summary>
internal sealed class AnomalibSessionOptionsFactory : IAnomalibSessionOptionsFactory
{
    /// <summary>CPU 发行包不提供 CUDA 会话。</summary>
    public bool SupportsSamCuda => false;
    /// <summary>仅接受 CPU，拒绝 GPU 请求，不静默降级。</summary>
    public OrtSessionOptions CreateSam(int? gpuId)
    {
        if (gpuId is not null) { throw new NotSupportedException("CPU 发行包不支持 SAM GPU 推理，请使用 CUDA 发行包。"); }
        return Create();
    }
    /// <summary>创建 CPU 执行会话选项。</summary>
    public OrtSessionOptions Create() => new()
    {
        GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL,
        EnableCpuMemArena = true,
        ExecutionMode = ExecutionMode.ORT_SEQUENTIAL,
    };
}
