namespace Snet.Yolo.Tasks.Core.Config;

using System;
using System.Collections.Generic;

/// <summary>控制标签信息。</summary>
public sealed class ControlTagInfo
{
    /// <summary>XML 元素名。</summary>
    public string TagName { get; init; } = string.Empty;
    /// <summary>种类。</summary>
    public ControlTagKind Kind { get; init; }
    /// <summary>name 属性（唯一）。</summary>
    public string Name { get; init; } = string.Empty;
    /// <summary>toName：绑定的对象标签名。</summary>
    public string? ToName { get; init; }
    /// <summary>choice 模式（single/multiple/single-radio），Choices 使用。</summary>
    public string? ChoiceMode { get; init; }
    /// <summary>perRegion（TextArea/Labels 附加到区域的模式）。</summary>
    public bool PerRegion { get; init; }
    /// <summary>标签选项（Label 子元素）。</summary>
    public List<LabelOptionInfo> Labels { get; } = new();
    /// <summary>选择项（Choice 子元素，Choices/Taxonomy）。</summary>
    public List<ChoiceOptionInfo> Choices { get; } = new();
    /// <summary>全部原始属性。</summary>
    public IReadOnlyDictionary<string, string> Attributes { get; init; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
}
