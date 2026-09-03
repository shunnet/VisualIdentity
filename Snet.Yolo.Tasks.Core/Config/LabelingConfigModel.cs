namespace Snet.Yolo.Tasks.Core.Config;

using System;
using System.Collections.Generic;
using System.Linq;

/// <summary>对象（数据展示）标签种类：对应 LS object tags。</summary>
public enum ObjectTagKind
{
    /// <summary>未识别。</summary>
    Unknown = 0,
    Image,
    Text,
    Audio,
    TimeSeries,
    Video,
    HyperText,
    Paragraphs,
}

/// <summary>控制（标注）标签种类：对应 LS control tags。</summary>
public enum ControlTagKind
{
    /// <summary>未识别。</summary>
    Unknown = 0,
    RectangleLabels,
    PolygonLabels,
    KeyPointLabels,
    EllipseLabels,
    BrushLabels,
    Labels,
    Choices,
    Rating,
    TextArea,
    Taxonomy,
    Relation,
    Number,
    Pairwise,
    Ranker,
    DateTime,
    Shortcut,
}

/// <summary>
/// 解析后的标签配置：<see cref="LabelingConfigParser"/> 的输出模型。
/// </summary>
public sealed class LabelingConfigModel
{
    /// <summary>对象标签（Image/Text/Audio…）。</summary>
    public List<ObjectTagInfo> Objects { get; } = new();

    /// <summary>控制标签（RectangleLabels/Labels/Choices…）。</summary>
    public List<ControlTagInfo> Controls { get; } = new();

    /// <summary>其余（视觉/结构）标签（Header/View/style 等，不参与标注语义）。</summary>
    public List<VisualTagInfo> Visuals { get; } = new();

    /// <summary>按名取对象标签。</summary>
    public ObjectTagInfo? FindObject(string name) => Objects.FirstOrDefault(o => o.Name == name);
}

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

/// <summary>Choice 选项。</summary>
public sealed class ChoiceOptionInfo
{
    /// <summary>选项值。</summary>
    public string Value { get; init; } = string.Empty;
    /// <summary>颜色（可空）。</summary>
    public string? Background { get; init; }
}

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

/// <summary>配置结构错误（XML 无法解析 / 根不是 View）。</summary>
public sealed class ConfigFormatException : Exception
{
    /// <summary>构造。</summary>
    public ConfigFormatException(string message) : base(message)
    {
    }
}
