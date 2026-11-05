using SessionOptions = Microsoft.ML.OnnxRuntime.SessionOptions;
using Snet.Yolo.Server.anomalib;
using Snet.Yolo.Server.inference;
using YoloDotNet.Models.Interfaces;

namespace Snet.Yolo.Api.Services;

/// <summary>一次 API 请求固定的硬件与缓存代次，避免切换期间混用配置。</summary>
/// <param name="settings">已保存的不可变硬件配置。</param>
/// <param name="generation">捕获配置时的 YOLO 缓存代次。</param>
public sealed class OnnxRuntimeSnapshot(OnnxRuntimeSettings settings, long generation) : IAnomalibSessionOptionsFactory
{
    /// <summary>本次请求使用的设备配置。</summary>
    public OnnxRuntimeSettings Settings { get; } = settings;
    /// <summary>旧请求延迟加载时只使用临时会话，不重新填充已退役缓存。</summary>
    public long Generation { get; } = generation;
    /// <summary>Anomalib 会话使用的硬件标识。</summary>
    public string CacheKey => Settings.CacheKey;

    /// <summary>为已捕获的硬件创建 Anomalib 会话选项。</summary>
    /// <returns>由会话加载器负责释放的选项。</returns>
    public SessionOptions Create() => Settings.Hardware == OnnxRuntimeHardware.Cpu
        ? OnnxSessionOptions.CreateCpu() : OnnxSessionOptions.CreateCuda(Settings.GpuId);

    /// <summary>按同一个硬件快照创建 YOLO 执行提供程序。</summary>
    /// <param name="modelPath">服务器授权的 ONNX 模型路径。</param>
    /// <param name="tensorRt">可选的、已限制文件访问范围的 TensorRT 配置。</param>
    /// <returns>由识别会话负责释放的提供程序。</returns>
    public IExecutionProvider CreateProvider(string modelPath, TensorRt? tensorRt = null)
        => Settings.Hardware == OnnxRuntimeHardware.Cpu
            ? new CpuExecutionProvider(modelPath) : new CudaExecutionProvider(modelPath, Settings.GpuId, tensorRt);
}
