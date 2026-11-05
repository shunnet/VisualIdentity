namespace Snet.Yolo.Server.anomalib;

/// <summary>一种设备配置下的模型会话及租约计数，所有状态由推理服务锁保护。</summary>
/// <param name="key">模型绝对路径。</param>
/// <param name="hardwareKey">不可变设备缓存标识。</param>
/// <param name="value">线程安全的延迟会话创建器。</param>
internal sealed class AnomalibSessionEntry(string key, string hardwareKey, Lazy<AnomalibModelSession> value)
{
    /// <summary>模型绝对路径。</summary>
    public string Key { get; } = key;
    /// <summary>该会话固定的设备配置标识。</summary>
    public string HardwareKey { get; } = hardwareKey;
    /// <summary>只创建一次的原生会话。</summary>
    public Lazy<AnomalibModelSession> Value { get; } = value;
    /// <summary>当前活动租约数。</summary>
    public int Users { get; set; }
    /// <summary>是否已退役，不再供新请求使用。</summary>
    public bool Retired { get; set; }
    /// <summary>是否已取得一次性的释放权。</summary>
    public bool Disposed { get; set; }
    /// <summary>等待所有活动租约结束的完成信号。</summary>
    public TaskCompletionSource<bool>? Idle { get; set; }
}
