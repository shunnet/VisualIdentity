namespace Snet.Yolo.Tasks.Core.Editing;

using Snet.Yolo.Tasks.Core.Models;

/// <summary>汇总项目中各标签的实际标注区域数。</summary>
public static class ProjectLabelStatistics
{
    /// <summary>读取各结果行类型对应的标签字段，并兼容旧数据使用的通用字段。</summary>
    public static Dictionary<string, int> Count(IEnumerable<AnnotationTask> tasks)
    {
        var counts = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var task in tasks)
        {
            foreach (var annotation in task.Annotations.Where(item => item.WasCancelled != true))
            {
                foreach (var row in annotation.Result)
                {
                    if (row.Value is null) { continue; }
                    var names = row.Type switch
                    {
                        RegionType.RectangleLabels or RegionType.PolygonLabels or RegionType.KeyPointLabels
                            or RegionType.EllipseLabels or RegionType.BrushLabels or RegionType.Labels or RegionType.Choices
                            => ValueAccess.GetStringList(row.Value, row.Type),
                        _ => [],
                    };
                    if (names.Count == 0) { names = ValueAccess.GetStringList(row.Value, RegionType.Labels); }
                    if (names.Count == 0) { names = ValueAccess.GetStringList(row.Value, RegionType.Choices); }
                    foreach (var name in names) { counts[name] = counts.GetValueOrDefault(name) + 1; }
                }
            }
        }
        return counts;
    }
}
