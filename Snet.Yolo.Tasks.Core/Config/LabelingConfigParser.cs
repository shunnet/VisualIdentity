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
        // Label Studio permits layout containers to be nested. Walk the complete
        // configuration tree while keeping option elements owned by their control.
        foreach (var element in root.Descendants().Where(element =>
                     element.Name.LocalName is not "View" and not "Label" and not "Choice"))
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
