using Snet.Yolo.Tasks.Core.Config;
using Snet.Yolo.Tasks.Core.Editing;
using Snet.Yolo.Tasks.Core.Models;
using Snet.Yolo.Tasks.Core.Serialization.Export;
using Xunit;

namespace Snet.Yolo.Test;

/// <summary>验证 Ultralytics 标签格式及嵌套 Label Studio 配置解析。</summary>
public sealed class YoloLabelExporterTests
{
    /// <summary>嵌套布局容器内的分类控件仍应被解析并推断为分类任务。</summary>
    [Fact]
    public void Parse_NestedViewFindsClassificationControl()
    {
        const string xml = """
            <View><Text name="text" value="$text"/><View><Choices name="sentiment" toName="text"><Choice value="Positive"/></Choices></View></View>
            """;

        var config = LabelingConfigParser.Parse(xml);

        Assert.Single(config.Controls);
        Assert.Equal(ControlTagKind.Choices, config.Controls[0].Kind);
        Assert.Equal(YoloTaskType.Classify, YoloTaskRegistry.FromConfig(config));
        Assert.True(ConfigValidator.Validate(config).IsValid);
    }

    /// <summary>OBB 标签必须使用类别加四个归一化角点，共九列。</summary>
    [Fact]
    public void Build_ObbUsesFourCornerFormat()
    {
        var task = TaskWith(new ResultRow
        {
            Type = RegionType.RectangleLabels,
            Value = new() { ["x"] = 10, ["y"] = 20, ["width"] = 30, ["height"] = 40, ["rotation"] = 0, ["rectanglelabels"] = new System.Text.Json.Nodes.JsonArray("box") },
        });

        var values = Tokens(YoloLabelExporter.Build(task, YoloTaskType.Obb, ["box"]));

        Assert.Equal(9, values.Length);
        Assert.Equal(new[] { "0", "0.1", "0.2", "0.4", "0.2", "0.4", "0.6", "0.1", "0.6" }, values);
    }

    /// <summary>多边形 points 百分比数组应直接转换为 0-1 坐标。</summary>
    [Fact]
    public void Build_PolygonExportsNormalizedPoints()
    {
        var task = TaskWith(new ResultRow
        {
            Type = RegionType.PolygonLabels,
            Value = new()
            {
                ["points"] = new System.Text.Json.Nodes.JsonArray(
                    new System.Text.Json.Nodes.JsonArray(10, 20),
                    new System.Text.Json.Nodes.JsonArray(40, 20),
                    new System.Text.Json.Nodes.JsonArray(40, 60)),
                ["polygonlabels"] = new System.Text.Json.Nodes.JsonArray("area"),
            },
        });

        Assert.Equal(new[] { "0", "0.1", "0.2", "0.4", "0.2", "0.4", "0.6" },
            Tokens(YoloLabelExporter.Build(task, YoloTaskType.Segment, ["area"])));
    }

    /// <summary>笔刷 RLE 应生成有效的归一化分割多边形。</summary>
    [Fact]
    public void Build_BrushExportsMaskHull()
    {
        const string config = """
            <View><Image name="image" value="$image"/><BrushLabels name="brush" toName="image"><Label value="mask"/></BrushLabels></View>
            """;
        var task = new AnnotationTask();
        var session = new LabelingSession(config, task);
        session.SetImageOriginalSize(100, 100);
        session.AddBrushMask([20d, 80d], [50d, 50d], 10, "mask");

        var values = Tokens(YoloLabelExporter.Build(task, YoloTaskType.Segment, ["mask"]));

        Assert.True(values.Length >= 7);
        Assert.Equal("0", values[0]);
        Assert.All(values.Skip(1).Select(double.Parse), value => Assert.InRange(value, 0d, 1d));
    }

    /// <summary>Pose 应按父矩形输出一个对象，并按配置顺序补齐关键点。</summary>
    [Fact]
    public void Build_PoseGroupsKeyPointsByParentRectangle()
    {
        var rectangle = new ResultRow
        {
            Id = "person-1",
            Type = RegionType.RectangleLabels,
            Value = new() { ["x"] = 10, ["y"] = 20, ["width"] = 30, ["height"] = 40, ["rectanglelabels"] = new System.Text.Json.Nodes.JsonArray("person") },
        };
        var nose = new ResultRow
        {
            ParentId = rectangle.Id,
            Type = RegionType.KeyPointLabels,
            Value = new() { ["x"] = 20, ["y"] = 30, ["keypointlabels"] = new System.Text.Json.Nodes.JsonArray("nose") },
        };
        var task = TaskWith(rectangle, nose);

        var values = Tokens(YoloLabelExporter.Build(task, YoloTaskType.Pose, ["person"], ["nose", "eye"]));

        Assert.Equal(11, values.Length);
        Assert.Equal(new[] { "0", "0.25", "0.4", "0.3", "0.4", "0.2", "0.3", "2", "0", "0", "0" }, values);
    }

    private static AnnotationTask TaskWith(params ResultRow[] rows) => new()
    {
        Annotations = [new Annotation { Result = [.. rows] }],
    };

    private static string[] Tokens(string text)
        => text.Trim().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
}
