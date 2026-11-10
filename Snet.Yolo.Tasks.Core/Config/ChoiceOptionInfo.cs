namespace Snet.Yolo.Tasks.Core.Config;

/// <summary>Choice 选项。</summary>
public sealed class ChoiceOptionInfo
{
    /// <summary>选项值。</summary>
    public string Value { get; init; } = string.Empty;
    /// <summary>颜色（可空）。</summary>
    public string? Background { get; init; }
}
