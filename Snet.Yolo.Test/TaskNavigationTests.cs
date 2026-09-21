using Snet.Yolo.Tasks.Components.Pages;
using Xunit;

namespace Snet.Yolo.Test;

/// <summary>验证标注页倒序任务导航对应的显示序号。</summary>
public sealed class TaskNavigationTests
{
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
