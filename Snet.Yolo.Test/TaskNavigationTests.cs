using Snet.Yolo.Tasks.Components.Layout;
using Snet.Yolo.Tasks.Components.Pages;
using Snet.Yolo.Tasks.Core.Models;
using Snet.Yolo.Tasks.Core.Workspace;
using System.Reflection;
using System.Text.Json.Nodes;
using Xunit;

namespace Snet.Yolo.Test;

/// <summary>验证任务显示序号及项目导航的选中状态。</summary>
public sealed class TaskNavigationTests
{
    /// <summary>同一项目切到下一张再返回时，继续使用用户选择的标签，而不是重置为第一项。</summary>
    [Theory]
    [InlineData("rect", "RectangleLabels")]
    [InlineData("polygon", "PolygonLabels")]
    [InlineData("brush", "BrushLabels")]
    [InlineData("ellipse", "EllipseLabels")]
    [InlineData("keypoint", "KeyPointLabels")]
    public async Task Editor_ImageNavigationKeepsSelectedLabel(string tool, string control)
    {
        var project = new WorkspaceProject
        {
            LabelConfigXml = $"<View><Image name='image' value='$image'/><{control} name='label' toName='image'><Label value='first'/><Label value='defect'/></{control}></View>",
            Tasks = [new() { Id = 1, Data = new JsonObject { ["image"] = "a.png" } }, new() { Id = 2, Data = new JsonObject { ["image"] = "b.png" } }],
        };
        var editor = new Editor { ProjectId = project.Id, Language = new() };
        const BindingFlags members = BindingFlags.Instance | BindingFlags.NonPublic;
        var type = typeof(Editor);
        type.GetField("_project", members)!.SetValue(editor, project);
        type.GetField("_activeTool", members)!.SetValue(editor, tool);
        Task Load() => (Task)type.GetMethod("OnParametersSetAsync", members)!.Invoke(editor, null)!;
        try
        {
            await Load();
            // 选择第二个标签，随后走路由切图实际使用的重置与加载流程。
            type.GetField("_activeLabelIndex", members)!.SetValue(editor, 1);
            foreach (var index in new[] { 1, 0 })
            {
                editor.TaskIndex = index;
                await Load();
                Assert.Same(project.Tasks[index], editor.Session!.Task);
                Assert.Equal("defect", type.GetMethod("ActiveLabelValue", members)!.Invoke(editor, null));
            }
        }
        finally
        {
            type.GetField("_project", members)!.SetValue(editor, null);
            await editor.DisposeAsync();
        }
    }

    /// <summary>项目列表、详情及训练页均应选中所属项目导航，不能误选其他菜单。</summary>
    [Theory]
    [InlineData("/projects", "/projects", true)]
    [InlineData("/projects", "/project/example", true)]
    [InlineData("/projects", "/train/example", true)]
    [InlineData("/projects", "/train/example?from=project", true)]
    [InlineData("/projects", "/anomalib-project/example", false)]
    [InlineData("/projects", "/anomalib-train/example", false)]
    [InlineData("/projects", "/validation", false)]
    [InlineData("/anomalib-projects", "/anomalib-projects", true)]
    [InlineData("/anomalib-projects", "/anomalib-project/example", true)]
    [InlineData("/anomalib-projects", "/anomalib-train/example", true)]
    [InlineData("/anomalib-projects", "/train/example", false)]
    [InlineData("/anomalib-projects", "/anomalib-validation", false)]
    [InlineData("/validation", "/validation", true)]
    [InlineData("/validation", "/train/example", false)]
    [InlineData("/joint-validation", "/train/example", false)]
    public void IsNavigationActive_MatchesOwningProject(string href, string path, bool expected)
    {
        Assert.Equal(expected, AppShellLayout.IsNavigationActive(href, path));
    }

    /// <summary>最新任务从 1 开始，点击下一页进入更旧任务时显示序号应递增。</summary>
    [Theory]
    [InlineData(24, 25, 1)]
    [InlineData(23, 25, 2)]
    [InlineData(22, 25, 3)]
    [InlineData(0, 25, 25)]
    public void ToReverseDisplayNumber_IncreasesWhenNavigatingToOlderTask(
        int taskIndex,
        int taskCount,
        int expected)
    {
        Assert.Equal(expected, Editor.ToReverseDisplayNumber(taskIndex, taskCount));
    }

    /// <summary>空任务列表不应显示不存在的任务序号。</summary>
    [Fact]
    public void ToReverseDisplayNumber_ReturnsZero_WhenTaskListIsEmpty()
    {
        Assert.Equal(0, Editor.ToReverseDisplayNumber(0, 0));
    }
}
