namespace Snet.Yolo.Tasks.Core.Config;

/// <summary>Label 选项（value/background/hotkey/alias/model_index）。</summary>
public sealed class LabelOptionInfo
{
    /// <summary>存储/导出值。</summary>
    public string Value { get; init; } = string.Empty;
    /// <summary>颜色（hex 或 rgba）。</summary>
    public string? Background { get; init; }
    /// <summary>快捷键（数字/字母）。</summary>
    public string? Hotkey { get; init; }
    /// <summary>别名（仅显示）。</summary>
    public string? Alias { get; init; }
    /// <summary>YOLO 关键点顺序索引。</summary>
    public int? ModelIndex { get; init; }
}
