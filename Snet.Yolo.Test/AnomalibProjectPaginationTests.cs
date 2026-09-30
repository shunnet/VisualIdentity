using System.Reflection;
using Microsoft.AspNetCore.Components;
using Snet.Yolo.Tasks.Components.Pages;
using Snet.Yolo.Tasks.Core.Models;
using Snet.Yolo.Tasks.Core.Workspace;
using Xunit;

namespace Snet.Yolo.Test;

/// <summary>验证 Anomalib 图片分页链接及页数减少后的边界，不依赖工程数据库。</summary>
public sealed class AnomalibProjectPaginationTests
{
    [Theory]
    [InlineData(0, 2, "")]
    [InlineData(150, 2, "")]
    [InlineData(151, 2, "?page=2")]
    [InlineData(301, 3, "?page=3")]
    [InlineData(300, 3, "?page=2")]
    [InlineData(301, -1, "")]
    [InlineData(301, int.MaxValue, "?page=3")]
    public void PageLinks_RetainRequestedPageWithinAvailableImages(int count, int requested, string query)
    {
        var page = new AnomalibProjectDetails();
        var members = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
        typeof(AnomalibProjectDetails).GetProperty("ProjectId", members)!.SetValue(page, "project-a");
        var project = new WorkspaceProject { Tasks = Enumerable.Range(0, count).Select(_ => new AnnotationTask()).ToList() };
        typeof(AnomalibProjectDetails).GetField("_project", members)!.SetValue(page, project);
        typeof(AnomalibProjectDetails).GetProperty("Navigation", members)!.SetValue(page, new TestNavigation());
        var url = (string)typeof(AnomalibProjectDetails).GetMethod("PageHref", members)!.Invoke(page, [requested])!;
        Assert.Equal("http://localhost/app/anomalib-project/project-a" + query, url);
    }

    private sealed class TestNavigation : NavigationManager
    {
        public TestNavigation() => Initialize("http://localhost/app/", "http://localhost/app/anomalib-project/project-a");
        protected override void NavigateToCore(string uri, bool forceLoad) => throw new NotSupportedException();
    }
}
