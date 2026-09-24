using Microsoft.ML.OnnxRuntime;
using OrtSessionOptions = Microsoft.ML.OnnxRuntime.SessionOptions;

namespace Snet.Yolo.Tasks.Services;

/// <summary>为 CUDA 发行包创建 Anomalib ONNX 会话；GPU 环境不可用时使用同包 CPU 路径。</summary>
internal sealed class AnomalibSessionOptionsFactory(ILogger<AnomalibSessionOptionsFactory> logger) : IAnomalibSessionOptionsFactory
{
    /// <summary>准备 CUDA 原生库并创建会话选项；环境失败时保留 CPU 提供程序。</summary>
    public OrtSessionOptions Create()
    {
        var options = new OrtSessionOptions
        {
            GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL,
            EnableCpuMemArena = true,
            ExecutionMode = ExecutionMode.ORT_SEQUENTIAL,
        };
        try
        {
            var reason = CudaRuntimeLibraries.TryPrepare(AppContext.BaseDirectory);
            if (reason is null) { options.AppendExecutionProvider_CUDA(0); }
            else { logger.LogWarning("Anomalib CUDA 推理不可用，使用 CPU：{Reason}", reason); }
        }
        catch (Exception error)
        {
            logger.LogWarning(error, "Anomalib CUDA 初始化失败，使用 CPU 推理。");
        }
        return options;
    }
}
