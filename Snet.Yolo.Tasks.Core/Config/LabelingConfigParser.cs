namespace Snet.Yolo.Tasks.Core.Config;

using Snet.Yolo.Tasks.Core.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;

/// <summary>
/// Labeling Config XML 解析与校验器。
/// 语义对齐上游 DataValidator：根须为 View、name 全局唯一、toName 必须指向已有对象标签、
/// 标签型控制至少含一个 Label/Choice。
/// </summary>
public static class LabelingConfigParser
{
    private static readonly Dictionary<string, ObjectTagKind> ObjectKinds = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Image"] = ObjectTagKind.Image,
        ["Text"] = ObjectTagKind.Text,
        ["Audio"] = ObjectTagKind.Audio,
        ["AudioPlus"] = ObjectTagKind.Audio,
        ["TimeSeries"] = ObjectTagKind.TimeSeries,
        ["Video"] = ObjectTagKind.Video,
        ["HyperText"] = ObjectTagKind.HyperText,
        ["Paragraphs"] = ObjectTagKind.Paragraphs,
    };

    private static readonly Dictionary<string, ControlTagKind> ControlKinds = new(StringComparer.OrdinalIgnoreCase)
    {
        ["RectangleLabels"] = ControlTagKind.RectangleLabels,
        ["Rectangle"] = ControlTagKind.RectangleLabels,
        ["PolygonLabels"] = ControlTagKind.PolygonLabels,
        ["Polygon"] = ControlTagKind.PolygonLabels,
        ["KeyPointLabels"] = ControlTagKind.KeyPointLabels,
        ["KeyPoint"] = ControlTagKind.KeyPointLabels,
        ["EllipseLabels"] = ControlTagKind.EllipseLabels,
        ["Ellipse"] = ControlTagKind.EllipseLabels,
        ["BrushLabels"] = ControlTagKind.BrushLabels,
        ["Brush"] = ControlTagKind.BrushLabels,
        ["Labels"] = ControlTagKind.Labels,
        ["Choices"] = ControlTagKind.Choices,
        ["Rating"] = ControlTagKind.Rating,
        ["TextArea"] = ControlTagKind.TextArea,
        ["Taxonomy"] = ControlTagKind.Taxonomy,
        ["Relation"] = ControlTagKind.Relation,
        ["Relations"] = ControlTagKind.Relation,
        ["Number"] = ControlTagKind.Number,
        ["Pairwise"] = ControlTagKind.Pairwise,
        ["Ranker"] = ControlTagKind.Ranker,
        ["DateTime"] = ControlTagKind.DateTime,
        ["Shortcut"] = ControlTagKind.Shortcut,
    };

    /// <summary>需要至少一个 Label/Choice 子项的控制标签集合。</summary>
    private static readonly HashSet<ControlTagKind> RequireOptionsKinds = new()
    {
        ControlTagKind.RectangleLabels, ControlTagKind.PolygonLabels, ControlTagKind.KeyPointLabels,
        ControlTagKind.EllipseLabels, ControlTagKind.BrushLabels, ControlTagKind.Labels,
        ControlTagKind.Choices, ControlTagKind.Taxonomy, ControlTagKind.Relation,
    };

    /// <summary>
    /// 解析标签配置 XML。
    /// </summary>
    /// <param name="xml">LS labeling config（根元素 View）。</param>
    /// <returns>解析模型（含结构校验问题列表）。</returns>
    /// <exception cref="ConfigFormatException">XML 非法或根元素不是 View 时抛出。</exception>
    public static LabelingConfigModel Parse(string xml)
    {
        XDocument document;
        try
        {
            document = XDocument.Parse(xml);
        }
        catch (System.Xml.XmlException error)
        {
            throw new ConfigFormatException("标签配置 XML 无法解析: " + error.Message);
        }

        var root = document.Root;
        if (root is null || root.Name.LocalName != "View")
        {
            throw new ConfigFormatException("标签配置根元素必须为 <View>。");
        }

        var model = new LabelingConfigModel
        {
            YoloTask = root.Attribute("yoloTask")?.Value,
        };
        foreach (var element in root.Elements())
        {
            var tagName = element.Name.LocalName;
            var attributes = element.Attributes().ToDictionary(a => a.Name.LocalName, a => a.Value, StringComparer.OrdinalIgnoreCase);

            if (ObjectKinds.TryGetValue(tagName, out var objectKind))
            {
                model.Objects.Add(new ObjectTagInfo
                {
                    TagName = tagName,
                    Kind = objectKind,
                    Name = GetAttr(attributes, "name") ?? string.Empty,
                    ValueField = GetAttr(attributes, "value"),
                    Attributes = attributes,
                });
                continue;
            }

            if (ControlKinds.TryGetValue(tagName, out var controlKind))
            {
                var control = new ControlTagInfo
                {
                    TagName = tagName,
                    Kind = controlKind,
                    Name = GetAttr(attributes, "name") ?? string.Empty,
                    ToName = GetAttr(attributes, "toName"),
                    ChoiceMode = GetAttr(attributes, "choice"),
                    PerRegion = string.Equals(GetAttr(attributes, "perRegion"), "true", StringComparison.OrdinalIgnoreCase),
                    Attributes = attributes,
                };

                foreach (var child in element.Elements())
                {
                    var childName = child.Name.LocalName;
                    var childAttrs = child.Attributes().ToDictionary(a => a.Name.LocalName, a => a.Value, StringComparer.OrdinalIgnoreCase);
                    if (childName == "Label")
                    {
                        control.Labels.Add(new LabelOptionInfo
                        {
                            Value = GetAttr(childAttrs, "value") ?? string.Empty,
                            Background = GetAttr(childAttrs, "background"),
                            Hotkey = GetAttr(childAttrs, "hotkey"),
                            Alias = GetAttr(childAttrs, "alias"),
                            ModelIndex = TryInt(GetAttr(childAttrs, "model_index")),
                        });
                    }
                    else if (childName == "Choice")
                    {
                        control.Choices.Add(new ChoiceOptionInfo
                        {
                            Value = GetAttr(childAttrs, "value") ?? string.Empty,
                            Background = GetAttr(childAttrs, "background"),
                        });
                    }
                }

                model.Controls.Add(control);
                continue;
            }

            model.Visuals.Add(new VisualTagInfo
            {
                TagName = tagName,
                Value = GetAttr(attributes, "value"),
                Attributes = attributes,
            });
        }

        return model;
    }

    /// <summary>
    /// 控制标签对应的 region result 类型（与 RegionType 常量一致）；
    /// 不产出 region 的控件返回 null。
    /// </summary>
    public static string? GetResultType(ControlTagKind kind) => kind switch
    {
        ControlTagKind.RectangleLabels => RegionType.RectangleLabels,
        ControlTagKind.PolygonLabels => RegionType.PolygonLabels,
        ControlTagKind.KeyPointLabels => RegionType.KeyPointLabels,
        ControlTagKind.EllipseLabels => RegionType.EllipseLabels,
        ControlTagKind.BrushLabels => RegionType.BrushLabels,
        ControlTagKind.Labels => RegionType.Labels,
        ControlTagKind.Choices => RegionType.Choices,
        ControlTagKind.TextArea => RegionType.TextArea,
        ControlTagKind.Rating => RegionType.Rating,
        ControlTagKind.Taxonomy => RegionType.Taxonomy,
        ControlTagKind.Number => RegionType.Number,
        ControlTagKind.Relation => RegionType.Relation,
        _ => null,
    };

    /// <summary>把属性解析成可空整数。</summary>
    private static int? TryInt(string? value)
        => int.TryParse(value, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var parsed) ? parsed : null;

    private static string? GetAttr(IReadOnlyDictionary<string, string> attributes, string name)
        => attributes.TryGetValue(name, out var value) ? value : null;
}

/// <summary>配置问题级别。</summary>
public enum ConfigIssueSeverity
{
    Error,
    Warning,
}

/// <summary>配置问题条目。</summary>
public sealed record ConfigIssue(ConfigIssueSeverity Severity, string Code, string Message);

/// <summary>校验结果。</summary>
public sealed class ConfigValidationResult
{
    /// <summary>问题列表。</summary>
    public List<ConfigIssue> Issues { get; } = new();

    /// <summary>是否可用（无 Error 级问题）。</summary>
    public bool IsValid => Issues.All(issue => issue.Severity != ConfigIssueSeverity.Error);

    /// <summary>Error 级问题数量。</summary>
    public int ErrorCount => Issues.Count(issue => issue.Severity == ConfigIssueSeverity.Error);
}

/// <summary>
/// 配置校验逻辑（独立于解析器，便于单元测试与复用）。
/// </summary>
public static class ConfigValidator
{
    private static readonly HashSet<ControlTagKind> RequireOptionsKinds = new()
    {
        ControlTagKind.RectangleLabels, ControlTagKind.PolygonLabels, ControlTagKind.KeyPointLabels,
        ControlTagKind.EllipseLabels, ControlTagKind.BrushLabels, ControlTagKind.Labels,
        ControlTagKind.Choices, ControlTagKind.Taxonomy, ControlTagKind.Relation,
    };

    /// <summary>对解析模型执行结构/语义校验。</summary>
    public static ConfigValidationResult Validate(LabelingConfigModel model)
    {
        var result = new ConfigValidationResult();

        if (model.Objects.Count == 0)
        {
            result.Issues.Add(new ConfigIssue(ConfigIssueSeverity.Error, "E001", "配置缺少对象标签（Image/Text/Audio 等）。"));
        }

        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var obj in model.Objects)
        {
            CheckUniqueName(result, names, "对象", obj.TagName, obj.Name);
            if (string.IsNullOrWhiteSpace(obj.Name))
            {
                result.Issues.Add(new ConfigIssue(ConfigIssueSeverity.Error, "E002", "对象标签缺少 name 属性: <" + obj.TagName + ">。"));
            }
        }

        foreach (var control in model.Controls)
        {
            CheckUniqueName(result, names, "控制", control.TagName, control.Name);

            if (string.IsNullOrWhiteSpace(control.Name))
            {
                result.Issues.Add(new ConfigIssue(ConfigIssueSeverity.Error, "E003", "控制标签缺少 name 属性: <" + control.TagName + ">。"));
                continue;
            }

            if (string.IsNullOrWhiteSpace(control.ToName))
            {
                if (control.Kind != ControlTagKind.Relation)
                {
                    result.Issues.Add(new ConfigIssue(ConfigIssueSeverity.Error, "E004", "控制标签 <" + control.TagName + " name=\"" + control.Name + "\"> 缺少 toName 属性。"));
                }
            }
            else if (model.FindObject(control.ToName) is null)
            {
                result.Issues.Add(new ConfigIssue(ConfigIssueSeverity.Error, "E005", "控制标签 <" + control.TagName + " name=\"" + control.Name + "\"> 的 toName \"" + control.ToName + "\" 未指向已存在的对象标签。"));
            }

            if (RequireOptionsKinds.Contains(control.Kind))
            {
                var optionCount = control.Labels.Count + control.Choices.Count;
                if (optionCount == 0)
                {
                    result.Issues.Add(new ConfigIssue(ConfigIssueSeverity.Warning, "E006w", "控制标签 <" + control.TagName + " name=\"" + control.Name + "\"> 暂无标签；请在「编辑标签」中添加。"));
                }
            }
        }

        return result;
    }

    private static void CheckUniqueName(ConfigValidationResult result, HashSet<string> names, string kind, string tagName, string name)
    {
        if (!string.IsNullOrWhiteSpace(name) && !names.Add(name))
        {
            result.Issues.Add(new ConfigIssue(ConfigIssueSeverity.Error, "E007", kind + "标签 name 重复: \"" + name + "\"（<" + tagName + ">）。"));
        }
    }
}
