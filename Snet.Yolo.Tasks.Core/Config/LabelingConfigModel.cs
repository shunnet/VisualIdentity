namespace Snet.Yolo.Tasks.Core.Config;

using System.Collections.Generic;
using System.Linq;

/// <summary>
/// 解析后的标签配置：<see cref="LabelingConfigParser"/> 的输出模型。
/// </summary>
public sealed class LabelingConfigModel
{
    /// <summary>Optional explicit YOLO task hint carried by the root View element.</summary>
    public string? YoloTask { get; set; }

    /// <summary>对象标签（Image/Text/Audio…）。</summary>
    public List<ObjectTagInfo> Objects { get; } = new();

    /// <summary>控制标签（RectangleLabels/Labels/Choices…）。</summary>
    public List<ControlTagInfo> Controls { get; } = new();

    /// <summary>其余（视觉/结构）标签（Header/View/style 等，不参与标注语义）。</summary>
    public List<VisualTagInfo> Visuals { get; } = new();

    /// <summary>按名取对象标签。</summary>
    public ObjectTagInfo? FindObject(string name) => Objects.FirstOrDefault(o => o.Name == name);
}
