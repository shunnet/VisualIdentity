using Microsoft.ML.OnnxRuntime;
using Snet.Yolo.Server.anomalib;

namespace Snet.Yolo.Test;

/// <summary>用 CPU 会话模拟硬件版本切换，便于在无 CUDA 机器验证缓存生命周期。</summary>
internal sealed class SwitchingAnomalibOptionsFactory : IAnomalibSessionOptionsFactory
{
    private int _created;
    /// <summary>模拟的当前设备缓存标识。</summary>
    public string CacheKey { get; set; } = "device-a";
    /// <summary>实际创建的原生会话选项数量。</summary>
    public int Created => Volatile.Read(ref _created);
    /// <summary>创建一份固定标识的工厂。</summary>
    /// <returns>不会随着当前选择变化的快照。</returns>
    public IAnomalibSessionOptionsFactory Capture() => new FixedAnomalibOptionsFactory(CacheKey, () => Interlocked.Increment(ref _created));
    /// <summary>创建当前设备选项。</summary>
    /// <returns>由调用者释放的 CPU 选项。</returns>
    public SessionOptions Create() => Capture().Create();
}
