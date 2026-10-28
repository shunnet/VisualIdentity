using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.JSInterop;
using Snet.Yolo.Server;
using Snet.Yolo.Server.models;
using Snet.Yolo.Server.models.data;
using Snet.Yolo.Tasks.Core.Models;
using Snet.Yolo.Tasks.Core.Workspace;
using Snet.Yolo.Tasks.Services;
using System.Security.Claims;
using System.Text.Json.Nodes;
using Xunit;

namespace Snet.Yolo.Test;

/// <summary>批量删除必须保持任务顺序、共享图片和并发保存保护。</summary>
[Collection("Database")]
public sealed class ProjectBatchDeleteTests
{
    private sealed class TestUser(string owner) : AuthenticationStateProvider
    {
        public override Task<AuthenticationState> GetAuthenticationStateAsync()
            => Task.FromResult(new AuthenticationState(new ClaimsPrincipal(new ClaimsIdentity([new(ClaimTypes.Name, owner)], "test"))));
    }

    private sealed class TestNavigation : NavigationManager
    {
        public TestNavigation() => Initialize("http://localhost/", "http://localhost/project/batch?page=2");
        protected override void NavigateToCore(string uri, bool forceLoad) => throw new NotSupportedException();
    }

    private sealed class TestJs : IJSRuntime
    {
        public string? ReplacedUrl { get; private set; }
        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args)
            => InvokeAsync<TValue>(identifier, CancellationToken.None, args);
        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken cancellationToken, object?[]? args)
        {
            if (identifier == "snetReplaceUrl") { ReplacedUrl = (string?)args?[0]; }
            return ValueTask.FromResult(default(TValue)!);
        }
    }

    [Theory]
    [InlineData(ProjectKind.Yolo)]
    [InlineData(ProjectKind.Anomalib)]
    public async Task DeleteSelected_PreservesSharedFilesAndRemovesUnreferencedFiles(ProjectKind kind)
    {
        var ct = TestContext.Current.CancellationToken;
        var owner = "batch-" + Guid.NewGuid().ToString("N");
        await using var projects = new ProjectOperate(owner + "-projects");
        await using var tasks = new ProjectTaskOperate(owner + "-tasks");
        var service = new WorkspaceService(projects, tasks, null!, new CurrentUserContext(new TestUser(owner)), NullLogger<WorkspaceService>.Instance);
        var project = new WorkspaceProject { Kind = kind, Name = "batch" };
        var root = Path.Combine(AppContext.BaseDirectory, "wwwroot", "data", "uploads", UserStoragePath.Segment(owner), project.Id);
        var sharedUrl = $"/uploads/{UserStoragePath.Segment(owner)}/{project.Id}/shared.jpg";
        var removedUrl = $"/uploads/{UserStoragePath.Segment(owner)}/{project.Id}/removed.jpg";
        var sharedAlias = sharedUrl.Replace("shared.jpg", OperatingSystem.IsWindows() ? "%73HARED.jpg" : "%73hared.jpg");
        project.Tasks = [Image(1, sharedUrl), Image(2, removedUrl), Image(3, sharedAlias)];
        Directory.CreateDirectory(root);
        await File.WriteAllTextAsync(Path.Combine(root, "shared.jpg"), "shared", ct);
        await File.WriteAllTextAsync(Path.Combine(root, "removed.jpg"), "removed", ct);
        await File.WriteAllTextAsync(Path.Combine(root, "removed.jpg.preview.jpg"), "preview", ct);
        try
        {
            await service.SaveProjectAsync(project, ct);
            var selected = new[] { project.Tasks[0], project.Tasks[1] };
            Assert.Equal(2, await service.DeleteTasksAsync(project, selected, ct));
            Assert.Equal(3, Assert.Single((await service.GetProjectAsync(project.Id, ct))!.Tasks).Id);
            Assert.True(File.Exists(Path.Combine(root, "shared.jpg")));
            Assert.False(File.Exists(Path.Combine(root, "removed.jpg")));
            Assert.False(File.Exists(Path.Combine(root, "removed.jpg.preview.jpg")));
            Assert.Equal(1, await service.DeleteTasksAsync(project, [project.Tasks[0]], ct));
            Assert.False(File.Exists(Path.Combine(root, "shared.jpg")));
            Assert.Empty((await service.GetProjectAsync(project.Id, ct))!.Tasks);
        }
        finally
        {
            var query = await projects.QueryAsync(owner, project.Id, ct);
            if (query.GetDetails(out List<ProjectData>? rows) && rows is { Count: > 0 })
            { await projects.DeleteAggregateAsync(rows[0].id, owner, project.Id, ct); }
            if (Directory.Exists(root)) { Directory.Delete(root, true); }
        }
    }

    [Fact]
    public async Task DeleteSelected_StaleSnapshotDoesNotDeleteTasksOrFiles()
    {
        var ct = TestContext.Current.CancellationToken;
        var owner = "batch-conflict-" + Guid.NewGuid().ToString("N");
        await using var projects = new ProjectOperate(owner + "-projects");
        await using var tasks = new ProjectTaskOperate(owner + "-tasks");
        var service = new WorkspaceService(projects, tasks, null!, new CurrentUserContext(new TestUser(owner)), NullLogger<WorkspaceService>.Instance);
        var project = new WorkspaceProject { Name = "batch", Tasks = [Image(1, "original.jpg"), Image(2, "other.jpg")] };
        try
        {
            await service.SaveProjectAsync(project, ct);
            var stale = (await service.GetProjectAsync(project.Id, ct))!;
            var latest = (await service.GetProjectAsync(project.Id, ct))!;
            latest.Tasks[0].Data!["updated"] = "yes";
            await service.SaveProjectAsync(latest, ct);
            await Assert.ThrowsAsync<InvalidOperationException>(() => service.DeleteTasksAsync(stale, [stale.Tasks[0]], ct));
            Assert.Equal(2, stale.Tasks.Count);
            var persisted = (await service.GetProjectAsync(project.Id, ct))!;
            Assert.Equal(2, persisted.Tasks.Count);
            Assert.Equal("yes", persisted.Tasks[0].Data!["updated"]!.ToString());
        }
        finally
        {
            var query = await projects.QueryAsync(owner, project.Id, ct);
            if (query.GetDetails(out List<ProjectData>? rows) && rows is { Count: > 0 })
            { await projects.DeleteAggregateAsync(rows[0].id, owner, project.Id, ct); }
        }
    }

    [Fact]
    public async Task DeleteSelected_UnsavedProjectIsNotCreated()
    {
        var ct = TestContext.Current.CancellationToken;
        var owner = "batch-unsaved-" + Guid.NewGuid().ToString("N");
        await using var projects = new ProjectOperate(owner + "-projects");
        await using var tasks = new ProjectTaskOperate(owner + "-tasks");
        var service = new WorkspaceService(projects, tasks, null!, new CurrentUserContext(new TestUser(owner)), NullLogger<WorkspaceService>.Instance);
        var project = new WorkspaceProject { Tasks = [Image(1, "test.jpg")] };
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.DeleteTasksAsync(project, [project.Tasks[0]], ct));
        Assert.Single(project.Tasks);
        Assert.Null(await service.GetProjectAsync(project.Id, ct));
    }

    [Fact]
    public async Task Yolo_DeleteLastPage_UpdatesUrlEvenWhenSearchPreviouslyUsedSameUrl()
    {
        var ct = TestContext.Current.CancellationToken;
        var owner = "batch-page-" + Guid.NewGuid().ToString("N");
        await using var projects = new ProjectOperate(owner + "-projects");
        await using var tasks = new ProjectTaskOperate(owner + "-tasks");
        var service = new WorkspaceService(projects, tasks, null!, new CurrentUserContext(new TestUser(owner)), NullLogger<WorkspaceService>.Instance);
        var project = new WorkspaceProject { Id = "batch", Tasks = Enumerable.Range(1, 21).Select(i => Image(i, $"{i}.png")).ToList() };
        try
        {
            await service.SaveProjectAsync(project, ct);
            var page = new Snet.Yolo.Tasks.Components.Pages.ProjectDetails();
            var members = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public;
            var type = page.GetType(); var js = new TestJs();
            type.GetProperty("ProjectId", members)!.SetValue(page, project.Id);
            type.GetProperty("Workspaces", members)!.SetValue(page, service);
            type.GetProperty("Navigation", members)!.SetValue(page, new TestNavigation());
            type.GetProperty("Language", members)!.SetValue(page, new Snet.Yolo.Tasks.Core.Localization.LanguageManager());
            type.GetProperty("Toast", members)!.SetValue(page, new ToastService());
            type.GetProperty("Js", members)!.SetValue(page, js);
            type.GetField("_project", members)!.SetValue(page, project);
            type.GetField("_page", members)!.SetValue(page, 2);
            type.GetField("_lastSyncedUrl", members)!.SetValue(page, "http://localhost/project/batch");
            var selected = new[] { project.Tasks[0], project.Tasks[1] };
            await (Task)type.GetMethod("DeleteSelectedTasksAsync", members)!.Invoke(page, [selected])!;
            Assert.Equal("http://localhost/project/batch", js.ReplacedUrl);
            Assert.Equal(19, (await service.GetProjectAsync(project.Id, ct))!.Tasks.Count);
        }
        finally
        {
            var query = await projects.QueryAsync(owner, project.Id, ct);
            if (query.GetDetails(out List<ProjectData>? rows) && rows is { Count: > 0 })
            { await projects.DeleteAggregateAsync(rows[0].id, owner, project.Id, ct); }
        }
    }

    private static AnnotationTask Image(long id, string url) => new() { Id = id, Data = new JsonObject { ["image"] = url } };
}
