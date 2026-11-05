using SessionOptions = Microsoft.ML.OnnxRuntime.SessionOptions;
using Snet.Yolo.Server.inference;

namespace Snet.Yolo.Tasks.Services;

/// <summary>将缓存标识与会话选项固定到同一设备配置。</summary>
/// <param name="settings">本次会话的设备快照。</param>
internal sealed class AnomalibSessionOptionsSnapshot(OnnxRuntimeSettings settings) : Snet.Yolo.Server.anomalib.IAnomalibSessionOptionsFactory
{
    /// <summary>该快照对应的硬件缓存标识。</summary>
    public string CacheKey { get; } = settings.CacheKey;
    /// <summary>按固定设备创建选项；CUDA 失败时保持明确失败。</summary>
    /// <returns>由调用方释放的选项。</returns>
    public SessionOptions Create()
    {
        if (settings.Hardware == OnnxRuntimeHardware.Cpu) { return OnnxSessionOptions.CreateCpu(); }
        var reason = CudaRuntimeLibraries.TryPrepare(AppContext.BaseDirectory);
        if (reason is not null) { throw new InvalidOperationException("Anomalib CUDA 环境不可用：" + reason); }
        return OnnxSessionOptions.CreateCuda(settings.GpuId);
    }
}
