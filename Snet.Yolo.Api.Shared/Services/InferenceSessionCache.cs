using Snet.Yolo.Server;

namespace Snet.Yolo.Api.Services;

/// <summary>
/// 在请求之间复用已初始化的识别会话。每个缓存的识别操作会在内部串行访问，
/// 避免每张图片都重新创建原生 ONNX 资源。
/// </summary>
public sealed class InferenceSessionCache : IDisposable, IAsyncDisposable
{
    private readonly Dictionary<string, InferenceSessionEntry> _sessions = new(StringComparer.Ordinal);
    private readonly object _sessionLock = new();
    private bool _disposed;
    private long _generation;

    /// <summary>当前硬件缓存代次；设备切换时递增。</summary>
    public long Generation { get { lock (_sessionLock) { return _generation; } } }

    /// <summary>获取已有会话，或以原子方式创建一次。</summary>
    /// <param name="key">包含执行提供程序、模型标识和执行设置的稳定缓存键。</param>
    /// <param name="factory">仅由成功创建会话的调用方执行的工厂方法。</param>
    /// <param name="generation">可选硬件代次；旧请求仅创建临时会话。</param>
    /// <returns>可复用的识别操作。</returns>
    public InferenceSessionLease Acquire(string key, Func<IdentityOperate> factory, long? generation = null)
    {
        InferenceSessionEntry entry;
        lock (_sessionLock)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (generation is not null && generation != _generation)
            {
                entry = new InferenceSessionEntry(key, new Lazy<IdentityOperate>(factory, LazyThreadSafetyMode.ExecutionAndPublication)) { Retired = true };
            }
            else if (!_sessions.TryGetValue(key, out entry!))
            {
                entry = new InferenceSessionEntry(key, new Lazy<IdentityOperate>(factory, LazyThreadSafetyMode.ExecutionAndPublication));
                _sessions.Add(key, entry);
            }
            entry.Users++;
        }
        try { return new InferenceSessionLease(this, entry, entry.Value.Value); }
        catch
        {
            Release(entry, failed: true);
            throw;
        }
    }

    /// <summary>模型元数据或文件变更后，移除并释放对应会话。</summary>
    /// <param name="providerTag">执行提供程序标识。</param>
    /// <param name="modelIndex">数据库中的模型下标。</param>
    public void Invalidate(string providerTag, int modelIndex)
    {
        var prefix = providerTag + ":" + modelIndex + ":";
        List<IdentityOperate> removed = [];
        lock (_sessionLock)
        {
            foreach (var key in _sessions.Keys.Where(key => key.StartsWith(prefix, StringComparison.Ordinal)).ToArray())
            {
                var entry = _sessions[key];
                _sessions.Remove(key);
                if (Retire(entry) is { } session) { removed.Add(session); }
            }
        }
        foreach (var session in removed) { try { session.Dispose(); } catch { } }
    }

    /// <summary>硬件切换时退役全部会话；活动租约结束后释放，不关闭缓存服务。</summary>
    public void InvalidateAll()
    {
        foreach (var session in RetireAll(dispose: false)) { try { session.Dispose(); } catch { } }
    }

    /// <summary>关闭缓存并延迟释放仍在使用的会话。</summary>
    public void Dispose()
    {
        foreach (var session in RetireAll()) { try { session.Dispose(); } catch { } }
    }

    /// <summary>异步关闭缓存并释放无活动请求的会话。</summary>
    /// <returns>异步释放任务。</returns>
    public async ValueTask DisposeAsync()
    {
        foreach (var session in RetireAll()) { try { await session.DisposeAsync(); } catch { } }
    }

    /// <summary>归还活动租约，最后一个请求释放已退役会话。</summary>
    /// <param name="entry">被租用的条目。</param>
    /// <param name="failed">会话创建是否失败。</param>
    internal void Release(InferenceSessionEntry entry, bool failed = false)
    {
        IdentityOperate? session;
        lock (_sessionLock)
        {
            if (failed && _sessions.TryGetValue(entry.Key, out var current) && ReferenceEquals(current, entry))
            {
                _sessions.Remove(entry.Key);
                entry.Retired = true;
            }
            entry.Users--;
            session = entry.Retired ? Retire(entry) : null;
        }
        if (session is not null) { try { session.Dispose(); } catch { } }
    }

    /// <summary>原子推进缓存代次，并取得可在锁外释放的会话。</summary>
    /// <param name="dispose">是否永久关闭缓存。</param>
    /// <returns>没有活动租约的会话。</returns>
    private List<IdentityOperate> RetireAll(bool dispose = true)
    {
        List<IdentityOperate> removed = [];
        lock (_sessionLock)
        {
            if (_disposed) { return removed; }
            _disposed = dispose;
            _generation++;
            foreach (var entry in _sessions.Values)
            {
                if (Retire(entry) is { } session) { removed.Add(session); }
            }
            _sessions.Clear();
        }
        return removed;
    }

    /// <summary>取得一个已空闲条目的一次性释放权。</summary>
    /// <param name="entry">待退役的缓存条目。</param>
    /// <returns>可以立即释放的会话；仍被使用时为空。</returns>
    private static IdentityOperate? Retire(InferenceSessionEntry entry)
    {
        entry.Retired = true;
        if (entry.Users != 0 || entry.Disposed) { return null; }
        entry.Disposed = true;
        return entry.Value.IsValueCreated ? entry.Value.Value : null;
    }

}
