using Snet.Yolo.Tasks.Components.Layout;
using Snet.Yolo.Tasks.Components.Pages;
using Xunit;

namespace Snet.Yolo.Test;

/// <summary>验证任务显示序号及项目导航的选中状态。</summary>
public sealed class TaskNavigationTests
{
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
