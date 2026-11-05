using SessionOptions = Microsoft.ML.OnnxRuntime.SessionOptions;
using Snet.Yolo.Server.inference;

namespace Snet.Yolo.Tasks.Services;

/// <summary>Anomalib 按全站设备创建会话，SAM 继续独立选择 CPU 或指定 GPU。</summary>
/// <param name="settings">全站验证硬件配置。</param>
internal sealed class AnomalibSessionOptionsFactory(OnnxRuntimeSettingsStore settings) : IAnomalibSessionOptionsFactory
{
    /// <summary>原生包是否提供 CUDA，独立于全站验证的设备选择。</summary>
    public bool SupportsSamCuda => OnnxSessionOptions.SupportsCuda;
    /// <summary>当前全站设备缓存标识。</summary>
    public string CacheKey => settings.Current.CacheKey;
    /// <summary>捕获设备与工厂，保证缓存标识和会话配置一致。</summary>
    /// <returns>配置固定的会话工厂。</returns>
    public Snet.Yolo.Server.anomalib.IAnomalibSessionOptionsFactory Capture() => new AnomalibSessionOptionsSnapshot(settings.Current);
    /// <summary>创建当前全站设备的选项。</summary>
    /// <returns>由调用方释放的选项。</returns>
    public SessionOptions Create() => Capture().Create();

    /// <summary>SAM 按自己的设备选择创建会话，不读取全站验证设备。</summary>
    /// <param name="gpuId">空值为 CPU，非负编号为指定 CUDA GPU。</param>
    /// <returns>由 SAM 运行时释放的选项。</returns>
    public SessionOptions CreateSam(int? gpuId)
    {
        if (gpuId is null) { return OnnxSessionOptions.CreateCpu(); }
        ArgumentOutOfRangeException.ThrowIfNegative(gpuId.Value);
        if (!SupportsSamCuda) { throw new NotSupportedException("当前平台原生包不支持 SAM GPU 推理。"); }
        var reason = CudaRuntimeLibraries.TryPrepare(AppContext.BaseDirectory);
        if (reason is not null) { throw new InvalidOperationException("SAM CUDA 环境不可用：" + reason); }
        return OnnxSessionOptions.CreateCuda(gpuId.Value);
    }
}
