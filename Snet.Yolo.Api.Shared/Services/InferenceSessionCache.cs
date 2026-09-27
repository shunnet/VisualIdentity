using Snet.Yolo.Server;

namespace Snet.Yolo.Api.Services;

/// <summary>
/// 在请求之间复用已初始化的识别会话。每个缓存的识别操作会在内部串行访问，
/// 避免每张图片都重新创建原生 ONNX 资源。
/// </summary>
public sealed class InferenceSessionCache : IDisposable, IAsyncDisposable
{
    private readonly Dictionary<string, SessionEntry> _sessions = new(StringComparer.Ordinal);
    private readonly object _sessionLock = new();
    private bool _disposed;

    /// <summary>获取已有会话，或以原子方式创建一次。</summary>
    /// <param name="key">包含执行提供程序、模型标识和执行设置的稳定缓存键。</param>
    /// <param name="factory">仅由成功创建会话的调用方执行的工厂方法。</param>
    /// <returns>可复用的识别操作。</returns>
    public SessionLease Acquire(string key, Func<IdentityOperate> factory)
    {
        SessionEntry entry;
        lock (_sessionLock)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (!_sessions.TryGetValue(key, out entry!))
            {
                entry = new SessionEntry(key, new Lazy<IdentityOperate>(factory, LazyThreadSafetyMode.ExecutionAndPublication));
                _sessions.Add(key, entry);
            }
            entry.Users++;
        }
        try { return new SessionLease(this, entry, entry.Value.Value); }
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

    /// <inheritdoc/>
    public void Dispose()
    {
        foreach (var session in RetireAll()) { try { session.Dispose(); } catch { } }
    }

    /// <inheritdoc/>
    public async ValueTask DisposeAsync()
    {
        foreach (var session in RetireAll()) { try { await session.DisposeAsync(); } catch { } }
    }

    private void Release(SessionEntry entry, bool failed = false)
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

    private List<IdentityOperate> RetireAll()
    {
        List<IdentityOperate> removed = [];
        lock (_sessionLock)
        {
            if (_disposed) { return removed; }
            _disposed = true;
            foreach (var entry in _sessions.Values)
            {
                if (Retire(entry) is { } session) { removed.Add(session); }
            }
            _sessions.Clear();
        }
        return removed;
    }

    private static IdentityOperate? Retire(SessionEntry entry)
    {
        entry.Retired = true;
        if (entry.Users != 0 || entry.Disposed) { return null; }
        entry.Disposed = true;
        return entry.Value.IsValueCreated ? entry.Value.Value : null;
    }

    internal sealed class SessionEntry(string key, Lazy<IdentityOperate> value)
    {
        public string Key { get; } = key;
        public Lazy<IdentityOperate> Value { get; } = value;
        public int Users { get; set; }
        public bool Retired { get; set; }
        public bool Disposed { get; set; }
    }

    /// <summary>保持识别会话有效，直至当前请求完成。</summary>
    public sealed class SessionLease : IDisposable
    {
        private InferenceSessionCache? _owner;
        private readonly SessionEntry _entry;

        internal SessionLease(InferenceSessionCache owner, SessionEntry entry, IdentityOperate operate)
        {
            _owner = owner;
            _entry = entry;
            Operate = operate;
        }

        /// <summary>当前请求使用的识别操作。</summary>
        public IdentityOperate Operate { get; }
        /// <summary>释放当前请求对识别会话的引用。</summary>
        public void Dispose() => Interlocked.Exchange(ref _owner, null)?.Release(_entry);
    }
}
