namespace Snet.Yolo.Tasks.Core.Config;

using System;
using System.Collections.Generic;

/// <summary>视觉/结构标签（Header 等）。</summary>
public sealed class VisualTagInfo
{
    /// <summary>XML 元素名。</summary>
    public string TagName { get; init; } = string.Empty;
    /// <summary>value 属性。</summary>
    public string? Value { get; init; }
    /// <summary>全部原始属性。</summary>
    public IReadOnlyDictionary<string, string> Attributes { get; init; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
}
