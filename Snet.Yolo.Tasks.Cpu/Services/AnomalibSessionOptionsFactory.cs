using Microsoft.ML.OnnxRuntime;
using OrtSessionOptions = Microsoft.ML.OnnxRuntime.SessionOptions;

namespace Snet.Yolo.Tasks.Services;

/// <summary>为 CPU 发行包创建经过图优化的 Anomalib ONNX 会话选项。</summary>
internal sealed class AnomalibSessionOptionsFactory : IAnomalibSessionOptionsFactory
{
    /// <summary>创建 CPU 执行会话选项。</summary>
    public OrtSessionOptions Create() => new()
    {
        GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL,
        EnableCpuMemArena = true,
        ExecutionMode = ExecutionMode.ORT_SEQUENTIAL,
    };
}
