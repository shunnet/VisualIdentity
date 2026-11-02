namespace Snet.Yolo.Tasks.Services;

/// <summary>每个页面电路的主题状态，使设置页和顶部快捷切换保持一致。</summary>
public sealed class ThemeManager
{
    /// <summary>当前主题。</summary>
    public string Current { get; private set; } = "light";
    /// <summary>主题变化通知。</summary>
    public event Action? Changed;
    /// <summary>更新经过允许列表校验的主题。</summary>
    public void SetTheme(string theme)
    {
        if (theme is not ("light" or "dark") || theme == Current) { return; }
        Current = theme;
        Changed?.Invoke();
    }
}
