using Microsoft.ML.OnnxRuntime;
using OrtSessionOptions = Microsoft.ML.OnnxRuntime.SessionOptions;

namespace Snet.Yolo.Tasks.Services;

/// <summary>为 CUDA 发行包创建 Anomalib ONNX 会话；GPU 环境不可用时使用同包 CPU 路径。</summary>
internal sealed class AnomalibSessionOptionsFactory(ILogger<AnomalibSessionOptionsFactory> logger) : IAnomalibSessionOptionsFactory
{
    /// <summary>CUDA 发行包支持 GPU；驱动和原生库在实际创建时验证。</summary>
    public bool SupportsSamCuda => true;
    /// <summary>为 SAM 创建 CPU 或指定单张 GPU 会话；GPU 初始化失败直接报告。</summary>
    public OrtSessionOptions CreateSam(int? gpuId)
    {
        if (gpuId < 0) { throw new ArgumentOutOfRangeException(nameof(gpuId)); }
        var options = new OrtSessionOptions { GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL, ExecutionMode = ExecutionMode.ORT_SEQUENTIAL };
        try
        {
            if (gpuId is { } id)
            {
                var reason = CudaRuntimeLibraries.TryPrepare(AppContext.BaseDirectory);
                if (reason is not null) { throw new InvalidOperationException("SAM CUDA 环境不可用：" + reason); }
                options.AppendExecutionProvider_CUDA(id);
            }
            return options;
        }
        catch { options.Dispose(); throw; }
    }
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
