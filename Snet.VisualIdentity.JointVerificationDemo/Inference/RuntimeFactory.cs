using Microsoft.ML.OnnxRuntime;
using Snet.Yolo.Server.anomalib;
using YoloDotNet.Models.Interfaces;
#if CUDA
using YoloDotNet.ExecutionProvider.Cuda;
#else
using YoloDotNet.ExecutionProvider.Cpu;
#endif

namespace Snet.VisualIdentity.JointVerificationDemo.Inference;

/// <summary>按构建类型创建执行提供程序；两类模型使用相同的 CPU 或 CUDA 配置。</summary>
internal sealed class RuntimeFactory(int gpuId) : IAnomalibSessionOptionsFactory
{
    /// <summary>当前构建使用的硬件名称，不将 GPU 初始化失败静默当作成功。</summary>
    public static string DeviceName =>
#if CUDA
        "CUDA";
#else
        "CPU";
#endif

    /// <summary>创建 Anomalib 会话选项，返回对象由 Server 推理服务释放。</summary>
    public SessionOptions Create()
    {
        var options = new SessionOptions { GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL };
        try
        {
#if CUDA
            options.AppendExecutionProvider_CUDA(gpuId);
#else
            _ = gpuId;
#endif
            return options;
        }
        catch { options.Dispose(); throw; }
    }

    /// <summary>创建并加载 YOLO 提供程序，生命周期交由联合推理引擎管理。</summary>
    /// <param name="path">YOLO ONNX 文件路径。</param>
    /// <returns>当前构建对应的执行提供程序。</returns>
    public IExecutionProvider CreateYolo(string path) =>
#if CUDA
        new CudaExecutionProvider(path, gpuId);
#else
        new CpuExecutionProvider(path);
#endif
}
