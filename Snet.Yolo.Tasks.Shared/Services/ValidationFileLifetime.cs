using System.Collections.Concurrent;

namespace Snet.Yolo.Tasks.Services;

/// <summary>跟踪当前应用实例创建的验证文件，并在宿主正常停止时清理这些文件。</summary>
public sealed class ValidationFileLifetime : IDisposable
{
    private readonly ConcurrentDictionary<string, byte> _files = new(StringComparer.OrdinalIgnoreCase);
    private int _disposed;

    /// <summary>登记一个已经成功写入的验证文件。</summary>
    public void Track(string path)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        _files.TryAdd(Path.GetFullPath(path), 0);
    }

    /// <summary>取消登记已经由用户主动删除的文件。</summary>
    public void Untrack(string path) => _files.TryRemove(Path.GetFullPath(path), out _);

    /// <summary>删除当前进程创建且仍然存在的验证文件，不影响其他应用实例。</summary>
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) { return; }
        foreach (var path in _files.Keys)
        {
            try { File.Delete(path); } catch { }
        }
        _files.Clear();
    }
}
