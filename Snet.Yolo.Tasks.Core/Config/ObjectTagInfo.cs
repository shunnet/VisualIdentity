namespace Snet.Yolo.Tasks.Core.Config;

using System;
using System.Collections.Generic;

/// <summary>对象标签信息。</summary>
public sealed class ObjectTagInfo
{
    /// <summary>XML 元素名（如 Image）。</summary>
    public string TagName { get; init; } = string.Empty;
    /// <summary>种类。</summary>
    public ObjectTagKind Kind { get; init; }
    /// <summary>name 属性（唯一）。</summary>
    public string Name { get; init; } = string.Empty;
    /// <summary>value 属性：data 字段引用（$image 等）。</summary>
    public string? ValueField { get; init; }
    /// <summary>全部原始属性（忽略大小写）。</summary>
    public IReadOnlyDictionary<string, string> Attributes { get; init; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
}
