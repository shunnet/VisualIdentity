namespace Snet.Yolo.Tasks.Services;

using Snet.Yolo.Server;
using Snet.Yolo.Server.models.data;
using Snet.Yolo.Tasks.Core.Models;
using Snet.Yolo.Tasks.Core.Workspace;
using System.Collections.Concurrent;
using System.Text.Json;

/// <summary>
/// 工程工作区服务：持久化迁移到 Snet.Yolo.Server（规范化 Project/Task 表）。
/// </summary>
public sealed class WorkspaceService
{
    private readonly ProjectOperate _projects;
    private readonly ProjectTaskOperate _tasks;
    private readonly TrainingService _training;
    private readonly ILogger<WorkspaceService> _logger;
    private readonly ConcurrentDictionary<string, int> _projectIds = new(StringComparer.Ordinal);

    public WorkspaceService(ProjectOperate projects, ProjectTaskOperate tasks, TrainingService training, ILogger<WorkspaceService> logger)
    {
        _projects = projects;
        _tasks = tasks;
        _training = training;
        _logger = logger;
    }

    public async Task<List<WorkspaceProject>> ListProjectsAsync(CancellationToken ct = default)
    {
        var q = await _projects.QueryAsync(ct);
        if (!q.GetDetails(out List<ProjectData>? list) || list is null) { return new(); }
        var result = new List<WorkspaceProject>();
        foreach (var p in list) { result.Add(await BuildProject(p, ct)); }
        return result.OrderByDescending(x => x.UpdatedAt).ToList();
    }

    public async Task<WorkspaceProject?> GetProjectAsync(string projectId, CancellationToken ct = default)
    {
        var q = await _projects.QueryAsync(projectId, ct);
        if (!q.GetDetails(out List<ProjectData>? list) || list is not { Count: > 0 }) { return null; }
        return await BuildProject(list[0], ct);
    }

    private async Task<WorkspaceProject> BuildProject(ProjectData p, CancellationToken ct)
    {
        _projectIds[p.projectId] = p.id;
        var wp = new WorkspaceProject
        {
            Id = p.projectId,
            Name = p.name,
            Description = p.describe,
            OverlayOpacity = p.overlayOpacity,
            LabelConfigXml = p.labelConfigXml,
            CreatedAt = p.createTime,
            UpdatedAt = p.updateTime,
        };
        var tq = await _tasks.QueryTasksAsync(p.id, ct);
        if (tq.GetDetails(out List<TaskData>? tasks))
        {
            wp.Tasks = (tasks ?? new()).OrderBy(t => t.taskIndex)
                .Select(t => DeserializeTask(t.dataJson)).OfType<AnnotationTask>().ToList();
        }
        return wp;
    }

    public async Task SaveProjectAsync(WorkspaceProject project, CancellationToken ct = default)
    {
        project.UpdatedAt = DateTime.UtcNow;
        var pd = new ProjectData { projectId = project.Id, name = project.Name, describe = project.Description, overlayOpacity = project.OverlayOpacity, labelConfigXml = project.LabelConfigXml };
        var find = await _projects.QueryAsync(project.Id, ct);
        if (find.GetDetails(out List<ProjectData>? exist) && exist is { Count: > 0 })
        {
            pd.id = exist[0].id;
            pd.createTime = exist[0].createTime;
            var updated = await _projects.UpdateAsync(pd, ct);
            if (!updated.Status) { throw new InvalidOperationException(updated.Message ?? $"Project '{project.Id}' could not be updated."); }
        }
        else
        {
            var add = await _projects.AddAsync(pd, ct);
            if (!add.Status) { throw new InvalidOperationException(add.Message ?? $"Project '{project.Id}' could not be created."); }
            var after = await _projects.QueryAsync(project.Id, ct);
            if (!after.GetDetails(out List<ProjectData>? list) || list is not { Count: > 0 }) { throw new InvalidOperationException($"Project '{project.Id}' could not be loaded after creation."); }
            pd.id = list[0].id;
        }
        _projectIds[project.Id] = pd.id;
        await PopulateTasks(pd.id, project, ct);
    }

    /// <summary>在调用线程立即生成不可变 JSON 快照，供后台保存队列使用。</summary>
    public string CreateTaskSnapshot(AnnotationTask task) => SerializeTask(task);

    /// <summary>只更新一个标注任务。翻页热路径不会再删除并重建工程的全部任务。</summary>
    public async Task SaveTaskSnapshotAsync(string projectId, int taskIndex, string taskJson, CancellationToken ct = default)
    {
        if (!_projectIds.TryGetValue(projectId, out var storageProjectId))
        {
            var query = await _projects.QueryAsync(projectId, ct);
            if (!query.GetDetails(out List<ProjectData>? projects) || projects is not { Count: > 0 })
            {
                throw new InvalidOperationException($"Project '{projectId}' was not found.");
            }
            storageProjectId = projects[0].id;
            _projectIds[projectId] = storageProjectId;
        }

        var result = await _tasks.UpdateTaskAsync(new TaskData
        {
            projectId = storageProjectId,
            taskIndex = taskIndex,
            dataJson = taskJson,
        }, ct);
        if (!result.Status) { throw new InvalidOperationException($"Task {taskIndex} could not be saved."); }
    }

    private async Task PopulateTasks(int projectId, WorkspaceProject project, CancellationToken ct)
    {
        // 批量：一次按工程删除 + 一次批量插入（2 次调用，避免 N+1 慢）
        var rows = new List<TaskData>();
        var i = 0;
        foreach (var task in project.Tasks)
        {
            rows.Add(new TaskData { projectId = projectId, taskIndex = i++, dataJson = SerializeTask(task) });
        }
        var saved = await _tasks.ReplaceTasksAsync(projectId, rows, ct);
        if (!saved.Status) { throw new InvalidOperationException(saved.Message ?? $"Tasks for project {projectId} could not be saved."); }
    }

    public async Task DeleteProjectAsync(string projectId, CancellationToken ct = default)
    {
        if (_training.IsActive(projectId)) { await _training.StopAsync(projectId); }
        var query = await _projects.QueryAsync(projectId, ct);
        if (!query.GetDetails(out List<ProjectData>? projects) || projects is not { Count: > 0 })
        {
            throw new InvalidOperationException($"Project '{projectId}' was not found.");
        }
        var projectResult = await _projects.DeleteAggregateAsync(projects[0].id, projectId, ct);
        if (!projectResult.Status) { throw new InvalidOperationException(projectResult.Message ?? $"Project '{projectId}' could not be deleted."); }
        _projectIds.TryRemove(projectId, out _);
        _training.ForgetProject(projectId);
        DeleteProjectFiles(projectId);
    }

    public void DeleteUploadedFiles(string projectId, IEnumerable<string?> imageUrls)
    {
        if (!IsSafeSegment(projectId)) { return; }
        var projectRoot = Path.GetFullPath(Path.Combine(UploadsRoot, projectId));
        foreach (var imageUrl in imageUrls)
        {
            if (string.IsNullOrWhiteSpace(imageUrl)) { continue; }
            var prefix = $"/uploads/{projectId}/";
            if (!imageUrl.StartsWith(prefix, StringComparison.Ordinal)) { continue; }
            var fileName = Uri.UnescapeDataString(imageUrl[prefix.Length..]);
            if (!IsSafeSegment(fileName)) { continue; }
            var file = Path.GetFullPath(Path.Combine(projectRoot, fileName));
            if (!file.StartsWith(projectRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) { continue; }
            try { File.Delete(file); }
            catch (Exception ex) { _logger.LogWarning(ex, "Could not delete uploaded image {ImagePath}", file); }
        }
    }

    private static string UploadsRoot => Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "wwwroot", "data", "uploads"));
    private static string TrainingRoot => Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "train"));

    private void DeleteProjectFiles(string projectId)
    {
        if (!IsSafeSegment(projectId)) { return; }
        DeleteDirectoryUnderRoot(UploadsRoot, projectId);
        DeleteDirectoryUnderRoot(TrainingRoot, projectId);
    }

    private void DeleteDirectoryUnderRoot(string root, string segment)
    {
        var directory = Path.GetFullPath(Path.Combine(root, segment));
        if (!directory.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) { return; }
        try { if (Directory.Exists(directory)) { Directory.Delete(directory, true); } }
        catch (Exception ex) { _logger.LogWarning(ex, "Could not delete project directory {ProjectDirectory}", directory); }
    }

    private static bool IsSafeSegment(string value) =>
        !string.IsNullOrWhiteSpace(value) && value == Path.GetFileName(value) && value is not "." and not "..";

    private static readonly JsonSerializerOptions TaskJsonOpts = new() { DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull };
    private static string SerializeTask(object task) => JsonSerializer.Serialize(task, TaskJsonOpts);
    private static AnnotationTask? DeserializeTask(string json) { try { return JsonSerializer.Deserialize<Snet.Yolo.Tasks.Core.Models.AnnotationTask>(json, TaskJsonOpts); } catch { return null; } }
}
