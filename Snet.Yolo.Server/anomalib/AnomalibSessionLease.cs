namespace Snet.Yolo.Server.anomalib;

/// <summary>保护活动推理使用的会话，设备切换或删除不能提前释放它。</summary>
/// <param name="owner">会话所属推理服务。</param>
/// <param name="entry">被租用的缓存条目。</param>
/// <param name="session">已成功加载的会话。</param>
internal sealed class AnomalibSessionLease(AnomalibOnnxInference owner, AnomalibSessionEntry entry, AnomalibModelSession session) : IDisposable
{
    private AnomalibOnnxInference? _owner = owner;
    /// <summary>租约有效期间可使用的原生会话及契约。</summary>
    public AnomalibModelSession Session { get; } = session;
    /// <summary>幂等归还租约，最后一个使用者负责释放已退役会话。</summary>
    public void Dispose() => Interlocked.Exchange(ref _owner, null)?.ReleaseLease(entry);
}
