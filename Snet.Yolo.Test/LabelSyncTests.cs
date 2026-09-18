using System.Text.Json.Nodes;
using System.Xml.Linq;
using Snet.Yolo.Tasks.Core.Config;
using Snet.Yolo.Tasks.Core.Editing;
using Snet.Yolo.Tasks.Core.Models;
using Snet.Yolo.Tasks.Core.Serialization.Export;
using Xunit;

namespace Snet.Yolo.Test;

/// <summary>
/// 编辑标签后必须同步已有标注：
///   · 改名：region 上的旧名跟着改成新名；
///   · 删除：引用该标签的 region 一并删除（标注页看不到框、统计不再计数、导出不会静默丢数据）；
///   · 历史遗留的"配置里已不存在的标签"也要被清掉（自愈）。
/// </summary>
public sealed class LabelSyncTests
{
    private const string ConfigWithTwoLabels = """
        <View>
          <Image name="image" value="$image" />
          <RectangleLabels name="rect" toName="image">
            <Label value="虫茧" background="#FF0000" />
            <Label value="烟叶" background="#00FF00" />
          </RectangleLabels>
        </View>
        """;

    private static LabelingConfigModel Parse(string xml) => LabelingConfigParser.Parse(xml);

    /// <summary>造一个矩形 region。</summary>
    private static ResultRow Rectangle(string id, params string[] labels)
    {
        var value = new JsonObject
        {
            ["x"] = 10d, ["y"] = 10d, ["width"] = 20d, ["height"] = 20d, ["rotation"] = 0d,
        };
        ValueAccess.SetStringList(value, RegionType.RectangleLabels, labels);
        return new ResultRow { Id = id, Type = RegionType.RectangleLabels, Value = value, FromName = "rect", ToName = "image" };
    }

    private static AnnotationTask TaskWith(params ResultRow[] rows)
        => new()
        {
            Id = 1,
            Annotations = new List<Annotation>
            {
                new() { Result = rows.ToList(), ResultCount = rows.Length },
            },
        };

    private static IReadOnlyList<ResultRow> Rows(AnnotationTask task) => task.Annotations[0].Result;

    [Fact]
    public void DeletedLabel_RemovesItsRegionsEverywhere()
    {
        var after = Parse(ConfigWithTwoLabels);
        var tasks = new[]
        {
            TaskWith(Rectangle("a", "虫茧"), Rectangle("b", "烟叶")),
            TaskWith(Rectangle("c", "虫茧")),
            TaskWith(Rectangle("d", "烟叶")),
        };

        // 新配置里删掉"虫茧"
        var trimmed = Parse(ConfigWithTwoLabels.Replace("<Label value=\"虫茧\" background=\"#FF0000\" />", string.Empty));
        var result = LabelSync.Apply(tasks, LabelSync.DetectRenames(after, trimmed), LabelSync.CollectNames(trimmed));

        Assert.Equal(0, result.RenamedRegions);
        Assert.Equal(2, result.RemovedRegions);          // a、c 两个框
        Assert.Equal(2, result.AffectedTasks);
        Assert.Single(Rows(tasks[0]));                   // 只留下"烟叶"那个
        Assert.Empty(Rows(tasks[1]));                    // 只剩"虫茧"的图片变成无标注
        Assert.Single(Rows(tasks[2]));
        Assert.Equal(Rows(tasks[1]).Count, tasks[1].Annotations[0].ResultCount);
    }

    [Fact]
    public void RenamedLabel_FollowsThroughToRegions()
    {
        var before = Parse(ConfigWithTwoLabels);
        // 把"烟叶"改名为"烟叶切丝"，颜色不变 → 视为改名而不是删除+新增
        var after = Parse(ConfigWithTwoLabels.Replace("value=\"烟叶\"", "value=\"烟叶切丝\""));
        var tasks = new[] { TaskWith(Rectangle("a", "烟叶"), Rectangle("b", "虫茧")) };

        var renames = LabelSync.DetectRenames(before, after);
        Assert.Equal("烟叶切丝", renames["烟叶"]);

        var result = LabelSync.Apply(tasks, renames, LabelSync.CollectNames(after));

        Assert.Equal(1, result.RenamedRegions);
        Assert.Equal(0, result.RemovedRegions);
        Assert.Equal(new[] { "烟叶切丝" }, ValueAccess.GetStringList(Rows(tasks[0])[0].Value!, RegionType.RectangleLabels));
        Assert.Equal(new[] { "虫茧" }, ValueAccess.GetStringList(Rows(tasks[0])[1].Value!, RegionType.RectangleLabels));
    }

    [Fact]
    public void SameNameButNewColor_IsNotTreatedAsRename()
    {
        var before = Parse(ConfigWithTwoLabels);
        // 名字保留、只是改了颜色 → 不该产生任何改名
        var after = Parse(ConfigWithTwoLabels.Replace("value=\"烟叶\" background=\"#00FF00\"", "value=\"烟叶\" background=\"#0000FF\""));
        Assert.Empty(LabelSync.DetectRenames(before, after));
    }

    [Fact]
    public void RenameWithColorChange_IsStillTreatedAsRename()
    {
        // 用户改名时顺手把颜色也改了：颜色对不上号，但"同控件里一对一替换"足以判定改名，标注不能丢
        var before = Parse(ConfigWithTwoLabels);
        var after = Parse(ConfigWithTwoLabels.Replace("value=\"烟叶\" background=\"#00FF00\"", "value=\"烟叶切丝\" background=\"#123456\""));
        var tasks = new[] { TaskWith(Rectangle("a", "烟叶")) };

        var renames = LabelSync.DetectRenames(before, after);
        var result = LabelSync.Apply(tasks, renames, LabelSync.CollectNames(after));

        Assert.Equal("烟叶切丝", renames["烟叶"]);
        Assert.Equal(1, result.RenamedRegions);
        Assert.Equal(0, result.RemovedRegions);
        Assert.Equal(new[] { "烟叶切丝" }, ValueAccess.GetStringList(Rows(tasks[0])[0].Value!, RegionType.RectangleLabels));
    }

    [Fact]
    public void DeletePlusAddTwoLabels_IsNotTreatedAsRename()
    {
        // 删掉一个、新增一个以上（不是一对一），就不能猜成改名，否则会张冠李戴
        var before = Parse(ConfigWithTwoLabels);
        var after = Parse(ConfigWithTwoLabels
            .Replace("<Label value=\"虫茧\" background=\"#FF0000\" />", string.Empty)
            .Replace("<Label value=\"烟叶\" background=\"#00FF00\" />", "<Label value=\"塑料\" background=\"#0000FF\" /><Label value=\"金属\" background=\"#FFFF00\" />"));

        Assert.Empty(LabelSync.DetectRenames(before, after));
    }
    [Fact]
    public void UnknownLegacyLabel_IsCleanedUpOnNextSave()
    {
        // 历史数据：配置里已经没有"塑料"，但旧标注还引用着它 → 一次保存就自愈
        var config = Parse(ConfigWithTwoLabels);
        var tasks = new[] { TaskWith(Rectangle("a", "塑料"), Rectangle("b", "烟叶")) };

        var result = LabelSync.Apply(tasks, new Dictionary<string, string>(), LabelSync.CollectNames(config));

        Assert.Equal(1, result.RemovedRegions);
        Assert.Single(Rows(tasks[0]));
        Assert.Equal("烟叶", ValueAccess.GetStringList(Rows(tasks[0])[0].Value!, RegionType.RectangleLabels)[0]);
    }

    [Fact]
    public void MultiLabelRegion_KeepsRemainingLabelsInsteadOfBeingDeleted()
    {
        var config = Parse(ConfigWithTwoLabels);
        var tasks = new[] { TaskWith(Rectangle("a", "虫茧", "烟叶"), Rectangle("b", "烟叶")) };

        // 只留下"烟叶"：多标签框应保留该框并去掉已删除的标签，而不是把框整条删掉
        var trimmed = Parse(ConfigWithTwoLabels.Replace("<Label value=\"虫茧\" background=\"#FF0000\" />", string.Empty));
        var trimmedNames = LabelSync.CollectNames(trimmed);
        var result = LabelSync.Apply(tasks, new Dictionary<string, string>(), trimmedNames);

        Assert.Equal(1, result.RenamedRegions);
        Assert.Equal(0, result.RemovedRegions);
        Assert.Equal(2, Rows(tasks[0]).Count);           // 两个框都保留
        Assert.Equal(new[] { "烟叶" }, ValueAccess.GetStringList(Rows(tasks[0])[0].Value!, RegionType.RectangleLabels));
        Assert.Equal(new[] { "烟叶" }, ValueAccess.GetStringList(Rows(tasks[0])[1].Value!, RegionType.RectangleLabels));
    }

    [Fact]
    public void ClassificationChoice_DisappearsWhenRemovedFromConfig()
    {
        const string classify = """
            <View>
              <Image name="image" value="$image" />
              <Choices name="choice" toName="image"><Choice value="合格" /><Choice value="不合格" /></Choices>
            </View>
            """;
        var value = new JsonObject();
        ValueAccess.SetStringList(value, RegionType.Choices, new[] { "不合格" });
        var task = new AnnotationTask
        {
            Id = 1,
            Annotations = new List<Annotation> { new() { Result = new List<ResultRow> { new() { Id = "c1", Type = RegionType.Choices, Value = value } }, ResultCount = 1 } },
        };

        var trimmed = Parse(classify.Replace("<Choice value=\"不合格\" />", string.Empty));
        var result = LabelSync.Apply(new[] { task }, new Dictionary<string, string>(), LabelSync.CollectNames(trimmed));

        Assert.Equal(1, result.RemovedRegions);
        Assert.Empty(task.Annotations[0].Result);
        Assert.Equal(0, task.Annotations[0].ResultCount);
    }

    [Fact]
    public void ColorChange_IsPickedUpEverywhereBecauseRegionsStoreOnlyTheName()
    {
        // 颜色只写在配置里、region 里只存标签名 → 改色后所有引用该标签的地方自动显示新颜色
        const string config = """
            <View>
              <Image name="image" value="$image" />
              <RectangleLabels name="rect" toName="image"><Label value="虫茧" background="#112233" /></RectangleLabels>
            </View>
            """;
        var task = TaskWith(Rectangle("a", "虫茧"));
        var session = new LabelingSession(config, task);
        session.SetImageOriginalSize(1000, 1000);

        Assert.Equal("#112233", session.BuildRegionViews().Single().Color);

        // 只改配置里的颜色（模拟"编辑标签"里改色）
        var recolored = new LabelingSession(config.Replace("#112233", "#ABCDEF"), task);
        recolored.SetImageOriginalSize(1000, 1000);

        Assert.Equal("#ABCDEF", recolored.BuildRegionViews().Single().Color);
        Assert.Equal("虫茧", recolored.BuildRegionViews().Single().LabelText);
    }
    [Fact]
    public void RemovingOneLabel_ShiftsIndicesToStayContiguousAndRewritesEveryRemainingBox()
    {
        // 3 个标签 L0/L1/L2，删掉中间的 L1：下标要变成连续的 0、1，且每个框都落到正确的类名上
        const string config = """
            <View>
              <Image name="image" value="$image" />
              <RectangleLabels name="rect" toName="image">
                <Label value="L0" background="#000001" />
                <Label value="L1" background="#000002" />
                <Label value="L2" background="#000003" />
              </RectangleLabels>
            </View>
            """;

        // 删除 L1 = 从配置里去掉它（编辑器保存后就是这样），再让 LabelSync 清掉它的框
        var after = Parse(config.Replace("    <Label value=\"L1\" background=\"#000002\" />\n", string.Empty)
                                .Replace("    <Label value=\"L1\" background=\"#000002\" />\r\n", string.Empty));
        var classes = after.Controls.SelectMany(control => control.Labels).Select(label => label.Value).Distinct().ToList();
        Assert.Equal(new[] { "L0", "L2" }, classes);          // 类别序列变连续（0、1）

        var onlyL0 = TaskWith(Rectangle("a", "L0"), Rectangle("b", "L1"));
        var onlyL1 = TaskWith(Rectangle("c", "L1"));
        var l2PlusL1 = TaskWith(Rectangle("d", "L2"), Rectangle("e", "L1"));
        var tasks = new[] { onlyL0, onlyL1, l2PlusL1 };

        var result = LabelSync.Apply(tasks, LabelSync.DetectRenames(Parse(config), after), LabelSync.CollectNames(after));
        Assert.Equal(3, result.RemovedRegions);               // 三个 L1 框都被删除
        Assert.Equal(0, result.RenamedRegions);               // 其余框存的还是名字，不需要改写

        // 导出时按下标写：L0→0、L2→1（前移），只剩 L1 的图片导出为空（背景图）
        var exportL0 = YoloLabelExporter.Build(onlyL0, YoloTaskType.Detect, classes);
        var exportOnlyL1 = YoloLabelExporter.Build(onlyL1, YoloTaskType.Detect, classes);
        var exportL2 = YoloLabelExporter.Build(l2PlusL1, YoloTaskType.Detect, classes);

        Assert.StartsWith("0 ", exportL0, StringComparison.Ordinal);        // L0 仍是 0
        Assert.StartsWith("1 ", exportL2, StringComparison.Ordinal);        // L2 从 2 前移到 1
        Assert.Equal(string.Empty, exportOnlyL1);                           // 该图变成无标注（训练里算背景图）
        Assert.Single(exportL0.Split('\n', StringSplitOptions.RemoveEmptyEntries));
        Assert.Single(exportL2.Split('\n', StringSplitOptions.RemoveEmptyEntries));
    }
    [Fact]
    public void NothingToDo_ReportsNoChanges()
    {
        var config = Parse(ConfigWithTwoLabels);
        var tasks = new[] { TaskWith(Rectangle("a", "虫茧"), Rectangle("b", "烟叶")) };

        var result = LabelSync.Apply(tasks, new Dictionary<string, string>(), LabelSync.CollectNames(config));

        Assert.False(result.Changed);
        Assert.Equal(2, Rows(tasks[0]).Count);
    }
}
