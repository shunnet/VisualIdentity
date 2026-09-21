using Snet.Yolo.Server;
using System.Collections.Concurrent;

namespace Snet.Yolo.Api.Services;

/// <summary>
/// Reuses initialized inference sessions between requests. Each cached operation
/// serializes access internally, so native ONNX resources are not recreated for every image.
/// </summary>
public sealed class InferenceSessionCache : IDisposable, IAsyncDisposable
{
    private readonly ConcurrentDictionary<string, Lazy<IdentityOperate>> _sessions = new(StringComparer.Ordinal);

    /// <summary>Gets an existing session or atomically creates it once.</summary>
    /// <param name="key">Stable key containing provider, model identity and execution settings.</param>
    /// <param name="factory">Factory used only by the winning caller.</param>
    /// <returns>The reusable inference operation.</returns>
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

    /// <summary>Removes and disposes sessions for a model after its metadata or file changes.</summary>
    /// <param name="providerTag">Execution provider tag.</param>
    /// <param name="modelIndex">Database model index.</param>
    public void Invalidate(string providerTag, int modelIndex)
    {
        var prefix = providerTag + ":" + modelIndex + ":";
        foreach (var item in _sessions)
        {
            if (!item.Key.StartsWith(prefix, StringComparison.Ordinal) ||
                !_sessions.TryRemove(new KeyValuePair<string, Lazy<IdentityOperate>>(item.Key, item.Value))) { continue; }
            // Access Value even when initialization is in flight. Lazy waits for the winning
            // factory, after which Dispose waits for any active inference before releasing it.
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
