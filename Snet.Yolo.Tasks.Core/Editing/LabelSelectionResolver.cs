namespace Snet.Yolo.Tasks.Core.Editing;

using Snet.Yolo.Tasks.Core.Config;
using Snet.Yolo.Tasks.Core.Models;

/// <summary>
/// 标注页左侧标签栏与右侧区域列表的选中同步规则：
/// 选中某个区域时，左侧标签栏应展示该区域所属控件的标签，并高亮区域自己的标签。
/// </summary>
public static class LabelSelectionResolver
{
    /// <summary>按优先级排列的几何控件：没有选中区域时依次选用第一个含标签的控件展示左侧标签。</summary>
    private static readonly ControlTagKind[] GeometryKinds =
    [
        ControlTagKind.RectangleLabels,
        ControlTagKind.PolygonLabels,
        ControlTagKind.KeyPointLabels,
        ControlTagKind.EllipseLabels,
        ControlTagKind.BrushLabels,
    ];

    /// <summary>把区域类型字符串映射为对应的标签控件类型。</summary>
    /// <param name="regionType">区域结果行的 <c>type</c> 值，见 <see cref="RegionType"/>。</param>
    /// <returns>几何区域返回对应控件类型；非几何或未知类型返回空。</returns>
    public static ControlTagKind? KindOf(string? regionType) => regionType switch
    {
        RegionType.RectangleLabels => ControlTagKind.RectangleLabels,
        RegionType.PolygonLabels => ControlTagKind.PolygonLabels,
        RegionType.KeyPointLabels => ControlTagKind.KeyPointLabels,
        RegionType.EllipseLabels => ControlTagKind.EllipseLabels,
        RegionType.BrushLabels => ControlTagKind.BrushLabels,
        _ => null,
    };

    /// <summary>在左侧标签列表中查找区域标签的位置。</summary>
    /// <param name="labels">左侧标签栏当前展示的标签。</param>
    /// <param name="regionLabel">选中区域的首个标签；区域没有标签时为空。</param>
    /// <returns>按值精确匹配的下标；区域无标签或标签不在列表中时返回 -1。</returns>
    public static int IndexOf(IReadOnlyList<LabelOptionInfo> labels, string? regionLabel)
    {
        ArgumentNullException.ThrowIfNull(labels);
        if (string.IsNullOrEmpty(regionLabel)) { return -1; }
        for (var index = 0; index < labels.Count; index++)
        {
            if (string.Equals(labels[index].Value, regionLabel, StringComparison.Ordinal)) { return index; }
        }
        return -1;
    }

    /// <summary>选择选择/平移工具时左侧标签栏应使用的控件类型。</summary>
    /// <param name="selectedRegionType">当前选中区域的类型；没有选中区域时为空。</param>
    /// <param name="hasLabels">判断某类控件是否配置了标签。</param>
    /// <returns>选中区域所属控件优先；否则取第一个含标签的几何控件；都没有时返回矩形控件。</returns>
    public static ControlTagKind ResolveKind(string? selectedRegionType, Func<ControlTagKind, bool> hasLabels)
    {
        ArgumentNullException.ThrowIfNull(hasLabels);
        if (KindOf(selectedRegionType) is { } selected) { return selected; }
        foreach (var kind in GeometryKinds)
        {
            if (hasLabels(kind)) { return kind; }
        }
        return ControlTagKind.RectangleLabels;
    }
}
