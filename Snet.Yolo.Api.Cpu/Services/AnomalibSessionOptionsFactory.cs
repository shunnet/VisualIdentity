using Microsoft.ML.OnnxRuntime;
using OrtSessionOptions = Microsoft.ML.OnnxRuntime.SessionOptions;
using Snet.Yolo.Server.Anomalib;

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
