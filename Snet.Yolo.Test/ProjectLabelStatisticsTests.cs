using System.Text.Json.Nodes;
using Snet.Yolo.Tasks.Core.Editing;
using Snet.Yolo.Tasks.Core.Models;
using Xunit;

namespace Snet.Yolo.Test;

/// <summary>验证项目标签统计读取各 YOLO 标注类型的真实标签字段。</summary>
public sealed class ProjectLabelStatisticsTests
{
    /// <summary>统计实际标注，兼容旧字段，同时排除取消的标注。</summary>
    [Fact]
    public void Count_ReadsTypedAndLegacyLabelsWithoutCancelledAnnotations()
    {
        var task = new AnnotationTask
        {
            Annotations =
            [
                new Annotation
                {
                    Result =
                    [
                        Row(RegionType.RectangleLabels, RegionType.RectangleLabels, "缺陷A"),
                        Row(RegionType.RectangleLabels, RegionType.RectangleLabels, "缺陷A"),
                        Row(RegionType.PolygonLabels, RegionType.PolygonLabels, "缺陷B"),
                        Row(RegionType.KeyPointLabels, RegionType.KeyPointLabels, "缺陷C"),
                        Row(RegionType.RectangleLabels, RegionType.Labels, "旧格式"),
                    ],
                },
                new Annotation { WasCancelled = true, Result = [Row(RegionType.RectangleLabels, RegionType.RectangleLabels, "不计入")] },
            ],
        };

        var counts = ProjectLabelStatistics.Count([task]);

        Assert.Equal(2, counts["缺陷A"]);
        Assert.Equal(1, counts["缺陷B"]);
        Assert.Equal(1, counts["缺陷C"]);
        Assert.Equal(1, counts["旧格式"]);
        Assert.DoesNotContain("不计入", counts.Keys);
    }

    /// <summary>创建一条使用指定标签字段的结果行。</summary>
    private static ResultRow Row(string type, string field, string label)
    {
        var value = new JsonObject();
        ValueAccess.SetStringList(value, field, [label]);
        return new ResultRow { Type = type, Value = value };
    }
}
