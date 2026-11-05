using Microsoft.ML.OnnxRuntime;
using SessionOptions = Microsoft.ML.OnnxRuntime.SessionOptions;
using Snet.Yolo.Server.anomalib;
using Snet.Yolo.Server.inference;

namespace Snet.Yolo.Api.Services;

/// <summary>统一管理 API 的硬件快照、持久化及缓存切换；不自动降级 GPU 请求。</summary>
/// <param name="store">与 Tasks 复用的原子配置存储。</param>
/// <param name="sessions">YOLO 请求间复用的识别会话。</param>
public sealed class OnnxRuntimeService(OnnxRuntimeSettingsStore store, InferenceSessionCache sessions) : IAnomalibSessionOptionsFactory
{
    private readonly object _sync = new();

    // IR 8 / opset 13 的一元素 Identity 模型；仅初始化执行提供程序，不下载业务权重。
    private static readonly byte[] ProbeModel = Convert.FromBase64String("CAg6OwoQCgF4EgF5IghJZGVudGl0eRIFcHJvYmVaDwoBeBIKCggIARIECgIIAWIPCgF5EgoKCAgBEgQKAggBQgQKABAN");

    /// <summary>已保存的配置；首次部署由 API 宿主指定为 GPU 0。</summary>
    public OnnxRuntimeSettings Current => store.Current;
    /// <summary>当前 Anomalib 硬件缓存标识。</summary>
    public string CacheKey => Current.CacheKey;

    /// <summary>原子捕获设备及代次，避免新设备复用旧的 YOLO 缓存。</summary>
    /// <returns>本次请求固定的硬件快照。</returns>
    public OnnxRuntimeSnapshot CaptureSnapshot()
    {
        lock (_sync) { return new OnnxRuntimeSnapshot(store.Current, sessions.Generation); }
    }

    /// <summary>为 Anomalib 缓存捕获不可变设备。</summary>
    /// <returns>不会随下一次配置变更而改变的工厂。</returns>
    public IAnomalibSessionOptionsFactory Capture() => CaptureSnapshot();

    /// <summary>创建当前 API 硬件的会话选项。</summary>
    /// <returns>由调用方释放的原生选项。</returns>
    public SessionOptions Create() => CaptureSnapshot().Create();

    /// <summary>验证实际 CUDA 会话后原子保存；准备或写入失败时保留旧配置和缓存。</summary>
    /// <param name="settings">需要启用的 CPU 或指定 CUDA GPU。</param>
    /// <param name="cancellationToken">在探测前及提交前取消操作。</param>
    /// <returns>本次操作成功提交的配置，不受随后的并发修改影响。</returns>
    public OnnxRuntimeSettings Configure(OnnxRuntimeSettings settings, CancellationToken cancellationToken = default)
    {
        settings.Validate();
        cancellationToken.ThrowIfCancellationRequested();
        // CUDA 初始化可能较慢，不占用快照读取锁；其它请求继续使用原配置。
        if (settings.Hardware == OnnxRuntimeHardware.Cuda)
        {
            if (!OnnxSessionOptions.SupportsCuda) { throw new NotSupportedException("当前平台原生包不支持 CUDA，请选择 CPU。"); }
            using var options = OnnxSessionOptions.CreateCuda(settings.GpuId);
            using var probe = new InferenceSession(ProbeModel, options);
        }
        lock (_sync)
        {
            cancellationToken.ThrowIfCancellationRequested();
            store.Save(settings);
            sessions.InvalidateAll();
            return settings;
        }
    }
}
