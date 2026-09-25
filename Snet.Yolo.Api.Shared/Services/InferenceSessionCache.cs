using Snet.Yolo.Server;
using System.Collections.Concurrent;

namespace Snet.Yolo.Api.Services;

/// <summary>
/// 在请求之间复用已初始化的识别会话。每个缓存的识别操作会在内部串行访问，
/// 避免每张图片都重新创建原生 ONNX 资源。
/// </summary>
public sealed class InferenceSessionCache : IDisposable, IAsyncDisposable
{
    private readonly ConcurrentDictionary<string, Lazy<IdentityOperate>> _sessions = new(StringComparer.Ordinal);

    /// <summary>获取已有会话，或以原子方式创建一次。</summary>
    /// <param name="key">包含执行提供程序、模型标识和执行设置的稳定缓存键。</param>
    /// <param name="factory">仅由成功创建会话的调用方执行的工厂方法。</param>
    /// <returns>可复用的识别操作。</returns>
    public IdentityOperate GetOrCreate(string key, Func<IdentityOperate> factory)
    {
        var lazy = _sessions.GetOrAdd(key, _ => new Lazy<IdentityOperate>(factory, LazyThreadSafetyMode.ExecutionAndPublication));
        try { return lazy.Value; }
        catch
        {
            _sessions.TryRemove(new KeyValuePair<string, Lazy<IdentityOperate>>(key, lazy));
            throw;
        }
    }

    /// <summary>模型元数据或文件变更后，移除并释放对应会话。</summary>
    /// <param name="providerTag">执行提供程序标识。</param>
    /// <param name="modelIndex">数据库中的模型下标。</param>
    public void Invalidate(string providerTag, int modelIndex)
    {
        var prefix = providerTag + ":" + modelIndex + ":";
        foreach (var item in _sessions)
        {
            if (!item.Key.StartsWith(prefix, StringComparison.Ordinal) ||
                !_sessions.TryRemove(new KeyValuePair<string, Lazy<IdentityOperate>>(item.Key, item.Value))) { continue; }
            // 即使初始化尚未完成也要访问 Value。Lazy 会等待正在创建会话的工厂方法，
            // 随后 Dispose 会等待正在执行的识别结束，再释放会话。
            try { item.Value.Value.Dispose(); } catch { }
        }
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        foreach (var item in _sessions.Values)
        {
            try { item.Value.Dispose(); } catch { }
        }
        _sessions.Clear();
    }

    /// <inheritdoc/>
    public async ValueTask DisposeAsync()
    {
        foreach (var item in _sessions.Values)
        {
            try { await item.Value.DisposeAsync(); } catch { }
        }
        _sessions.Clear();
    }
}
