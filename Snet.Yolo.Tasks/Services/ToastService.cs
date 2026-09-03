
namespace Snet.Yolo.Tasks.Services;

/// <summary>Toast 条目类型。</summary>
public enum ToastType { Info, Success, Warning, Error }

/// <summary>一条 Toast 消息。</summary>
public sealed record ToastEntry(string Id, string Message, ToastType Type);

/// <summary>
/// 全局顶部通知服务：任意组件调用 Show() 弹出提示，5 秒后由 <see cref="Toasts"/> 组件自动移除。
/// Blazor Server 中注册为 Scoped（每电路一份）。
/// </summary>
public sealed class ToastService
{
    private readonly object _lock = new();
    private readonly List<ToastEntry> _entries = new();

    /// <summary>当前消息列表（最新在前）。</summary>
    public IReadOnlyList<ToastEntry> Entries
    {
        get { lock (_lock) { return _entries.ToList(); } }
    }

    /// <summary>列表变化事件。</summary>
    public event Action? Changed;

    /// <summary>弹出提示（同文案只保留一条，避免叠加）。</summary>
    public void ShowReplacing(string message, ToastType type = ToastType.Info)
    {
        if (string.IsNullOrWhiteSpace(message)) { return; }
        lock (_lock)
        {
            _entries.RemoveAll(e => e.Message == message);
            _entries.Insert(0, new ToastEntry(Guid.NewGuid().ToString("N"), message, type));
        }
        Changed?.Invoke();
    }

    /// <summary>弹出提示。</summary>
    public void Show(string message, ToastType type = ToastType.Info)
    {
        if (string.IsNullOrWhiteSpace(message)) { return; }
        lock (_lock)
        {
            _entries.Insert(0, new ToastEntry(Guid.NewGuid().ToString("N"), message, type));
        }
        Changed?.Invoke();
    }

    /// <summary>按 id 移除一条。</summary>
    public void ShowError(string message) => Show(message, ToastType.Error);
    public void ShowSuccess(string message) => Show(message, ToastType.Success);
    public void ShowWarning(string message) => Show(message, ToastType.Warning);

    internal void Remove(string id)
    {
        lock (_lock)
        {
            _entries.RemoveAll(entry => entry.Id == id);
        }
        Changed?.Invoke();
    }
}
