namespace Snet.Yolo.Tasks.Core.Config;

using System;
using System.Collections.Generic;
using System.Linq;

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
            else if (control.ToName.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                     .Any(target => model.FindObject(target) is null && model.Controls.All(candidate => candidate.Name != target)))
            {
                result.Issues.Add(new ConfigIssue(ConfigIssueSeverity.Error, "E005", "控制标签 <" + control.TagName + " name=\"" + control.Name + "\"> 的 toName \"" + control.ToName + "\" 未指向已存在的对象或控制标签。"));
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
