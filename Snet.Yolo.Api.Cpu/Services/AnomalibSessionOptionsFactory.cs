using Microsoft.ML.OnnxRuntime;
using Snet.Yolo.Server.anomalib;
using OrtSessionOptions = Microsoft.ML.OnnxRuntime.SessionOptions;

namespace Snet.Yolo.Api.Services;

/// <summary>为 Anomalib API 识别创建 CPU ONNX 会话。</summary>
internal sealed class AnomalibSessionOptionsFactory : IAnomalibSessionOptionsFactory
{
    public OrtSessionOptions Create() => new()
    {
        GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL,
        EnableCpuMemArena = true,
        ExecutionMode = ExecutionMode.ORT_SEQUENTIAL,
    };
}
