namespace Snet.Yolo.Tasks.Services;

using Snet.Yolo.Server;
using Snet.Yolo.Server.models.data;
using Snet.Yolo.Server.models;
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
    private readonly AnomalibWorkflowService? _anomalibWorkflow;
    private readonly CurrentUserContext _currentUser;
    private readonly ILogger<WorkspaceService> _logger;
    private readonly ConcurrentDictionary<string, int> _projectIds = new(StringComparer.Ordinal);
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> ProjectWriteLocks = new(StringComparer.Ordinal);

    public WorkspaceService(ProjectOperate projects, ProjectTaskOperate tasks, TrainingService training, CurrentUserContext currentUser, ILogger<WorkspaceService> logger, AnomalibWorkflowService? anomalibWorkflow = null)
    {
        _projects = projects;
        _tasks = tasks;
        _training = training;
        _anomalibWorkflow = anomalibWorkflow;
        _currentUser = currentUser;
        _logger = logger;
    }

    /// <summary>列出当前用户指定类型的工程；默认仅返回历史兼容的 YOLO 工程。</summary>
    /// <param name="kind">需要列出的工程类型。</param>
    /// <param name="ct">取消令牌。</param>
    public async Task<List<WorkspaceProject>> ListProjectsAsync(ProjectKind kind = ProjectKind.Yolo, CancellationToken ct = default)
    {
        var owner = await _currentUser.GetRequiredUserNameAsync();
        var q = await _projects.QueryByOwnerAsync(owner, kind, ct);
        if (!q.GetDetails(out List<ProjectData>? list) || list is null) { return new(); }
        var result = new List<WorkspaceProject>();
        foreach (var p in list) { result.Add(await BuildProject(p, ct)); }
        return result.OrderByDescending(x => x.UpdatedAt).ToList();
    }

    /// <summary>只读取当前用户指定类型的工程名称，不加载任务与图片。</summary>
    public async Task<IReadOnlyDictionary<string, string>> ListProjectNamesAsync(ProjectKind kind, CancellationToken ct = default)
    {
        var owner = await _currentUser.GetRequiredUserNameAsync();
        var query = await _projects.QueryByOwnerAsync(owner, kind, ct);
        return query.GetDetails(out List<ProjectData>? projects) && projects is not null
            ? projects.ToDictionary(project => project.projectId, project => project.name, StringComparer.Ordinal)
            : new Dictionary<string, string>(StringComparer.Ordinal);
    }

    public async Task<WorkspaceProject?> GetProjectAsync(string projectId, CancellationToken ct = default)
        => await GetProjectForOwnerAsync(await _currentUser.GetRequiredUserNameAsync(), projectId, ct);

    /// <summary>读取当前用户且类型匹配的工程，类型不匹配时返回空。</summary>
    /// <param name="projectId">工程唯一标识。</param>
    /// <param name="kind">期望的工程类型。</param>
    /// <param name="ct">取消令牌。</param>
    public async Task<WorkspaceProject?> GetProjectAsync(string projectId, ProjectKind kind, CancellationToken ct = default)
    {
        var project = await GetProjectAsync(projectId, ct);
        return project?.Kind == kind ? project : null;
    }

    internal async Task<WorkspaceProject?> GetProjectForOwnerAsync(string owner, string projectId, CancellationToken ct = default)
    {
        var q = await _projects.QueryAsync(owner, projectId, ct);
        if (!q.GetDetails(out List<ProjectData>? list) || list is not { Count: > 0 }) { return null; }
        EnsureLegacyProjectDirectory(owner, projectId);
        return await BuildProject(list[0], ct);
    }

    private async Task<WorkspaceProject> BuildProject(ProjectData p, CancellationToken ct)
    {
        _projectIds[CacheKey(p.owner, p.projectId)] = p.id;
        var wp = new WorkspaceProject
        {
            Id = p.projectId,
            Name = p.name,
            Description = p.describe,
            Kind = p.kind,
            OverlayOpacity = p.overlayOpacity,
            LabelConfigXml = p.labelConfigXml,
            CreatedAt = p.createTime,
            UpdatedAt = p.updateTime,
        };
        var tq = await _tasks.QueryTasksAsync(p.id, ct);
        if (tq.GetDetails(out List<TaskData>? tasks))
        {
            wp.Tasks = new List<AnnotationTask>();
            foreach (var taskRow in (tasks ?? new()).OrderBy(task => task.taskIndex))
            {
                wp.Tasks.Add(DeserializeTask(taskRow));
            }
        }
        return wp;
    }

    public async Task SaveProjectAsync(WorkspaceProject project, CancellationToken ct = default)
    {
        var owner = await _currentUser.GetRequiredUserNameAsync();
        var key = CacheKey(owner, project.Id);
        var writeLock = ProjectWriteLocks.GetOrAdd(key, static _ => new SemaphoreSlim(1, 1));
        await writeLock.WaitAsync(ct);
        try
        {
            project.UpdatedAt = DateTime.UtcNow;
            var pd = new ProjectData { owner = owner, projectId = project.Id, name = project.Name, describe = project.Description, kind = project.Kind, overlayOpacity = project.OverlayOpacity, labelConfigXml = project.LabelConfigXml };
            var find = await _projects.QueryAsync(owner, project.Id, ct);
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
                var after = await _projects.QueryAsync(owner, project.Id, ct);
                if (!after.GetDetails(out List<ProjectData>? list) || list is not { Count: > 0 }) { throw new InvalidOperationException($"Project '{project.Id}' could not be loaded after creation."); }
                pd.id = list[0].id;
            }
            _projectIds[key] = pd.id;
            await PopulateTasks(pd.id, project, ct);
        }
        finally { writeLock.Release(); }
    }

    /// <summary>在调用线程立即生成不可变 JSON 快照，供后台保存队列使用。</summary>
    public string CreateTaskSnapshot(AnnotationTask task) => SerializeTask(task);

    /// <summary>只更新一个标注任务。翻页热路径不会再删除并重建工程的全部任务。</summary>
    public async Task SaveTaskSnapshotAsync(string projectId, int taskIndex, long? taskId, string taskJson, CancellationToken ct = default)
    {
        var owner = await _currentUser.GetRequiredUserNameAsync();
        var key = CacheKey(owner, projectId);
        var writeLock = ProjectWriteLocks.GetOrAdd(key, static _ => new SemaphoreSlim(1, 1));
        await writeLock.WaitAsync(ct);
        try
        {
            if (!_projectIds.TryGetValue(key, out var storageProjectId))
            {
                var query = await _projects.QueryAsync(owner, projectId, ct);
                if (!query.GetDetails(out List<ProjectData>? projects) || projects is not { Count: > 0 })
                {
                    throw new InvalidOperationException($"Project '{projectId}' was not found.");
                }
                storageProjectId = projects[0].id;
                _projectIds[key] = storageProjectId;
            }

            var currentRows = await _tasks.QueryTasksAsync(storageProjectId, ct);
            if (!currentRows.GetDetails(out List<TaskData>? rows) || rows is null)
            {
                throw new InvalidOperationException($"Tasks for project '{projectId}' could not be loaded.");
            }
            var currentRow = rows.SingleOrDefault(row => row.taskIndex == taskIndex)
                ?? throw new InvalidOperationException($"Task {taskIndex} no longer exists. Reload the project before saving.");
            var currentTask = DeserializeTask(currentRow);
            if (taskId.HasValue && currentTask.Id != taskId)
            {
                throw new InvalidOperationException("The task order changed in another session. Reload the project before saving.");
            }

            var result = await _tasks.UpdateTaskAsync(new TaskData
            {
                projectId = storageProjectId,
                taskIndex = taskIndex,
                dataJson = taskJson,
            }, ct);
            if (!result.Status) { throw new InvalidOperationException($"Task {taskIndex} could not be saved."); }
        }
        finally { writeLock.Release(); }
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
        var owner = await _currentUser.GetRequiredUserNameAsync();
        var key = CacheKey(owner, projectId);
        var writeLock = ProjectWriteLocks.GetOrAdd(key, static _ => new SemaphoreSlim(1, 1));
        await writeLock.WaitAsync(ct);
        try
        {
            if (_training.IsActive(owner, projectId)) { await _training.StopAsync(owner, projectId); }
            if (_anomalibWorkflow is not null) { await _anomalibWorkflow.StopAndWaitAsync(owner, projectId, ct); }
            var query = await _projects.QueryAsync(owner, projectId, ct);
            if (!query.GetDetails(out List<ProjectData>? projects) || projects is not { Count: > 0 })
            {
                throw new InvalidOperationException($"Project '{projectId}' was not found.");
            }
            var projectResult = await _projects.DeleteAggregateAsync(projects[0].id, owner, projectId, ct);
            if (!projectResult.Status) { throw new InvalidOperationException(projectResult.Message ?? $"Project '{projectId}' could not be deleted."); }
            _projectIds.TryRemove(key, out _);
            _training.ForgetProject(owner, projectId);
            DeleteProjectFiles(owner, projectId);
        }
        finally
        {
            writeLock.Release();
        }
    }

    public async Task DeleteUploadedFilesAsync(string projectId, IEnumerable<string?> imageUrls)
    {
        var owner = await _currentUser.GetRequiredUserNameAsync();
        if (!IsSafeSegment(projectId)) { return; }
        var ownerSegment = UserStoragePath.Segment(owner);
        var projectRoot = Path.GetFullPath(Path.Combine(UploadsRoot, ownerSegment, projectId));
        foreach (var imageUrl in imageUrls)
        {
            if (string.IsNullOrWhiteSpace(imageUrl)) { continue; }
            var prefix = $"/uploads/{ownerSegment}/{projectId}/";
            if (string.Equals(owner, "snet", StringComparison.OrdinalIgnoreCase) && imageUrl.StartsWith($"/uploads/{projectId}/", StringComparison.Ordinal))
            {
                prefix = $"/uploads/{projectId}/";
            }
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

    private void DeleteProjectFiles(string owner, string projectId)
    {
        if (!IsSafeSegment(projectId)) { return; }
        var ownerSegment = UserStoragePath.Segment(owner);
        DeleteDirectoryUnderRoot(Path.Combine(UploadsRoot, ownerSegment), projectId);
        DeleteDirectoryUnderRoot(Path.Combine(TrainingRoot, "users", ownerSegment), projectId);
        DeleteDirectoryUnderRoot(Path.Combine(TrainingRoot, "anomalib", "users", ownerSegment), projectId);
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

    /// <summary>返回当前用户的工程图片目录及受保护访问前缀。</summary>
    public async ValueTask<(string Directory, string UrlPrefix)> GetProjectUploadLocationAsync(string projectId)
    {
        if (!IsSafeSegment(projectId)) { throw new ArgumentException("工程标识无效。", nameof(projectId)); }
        var owner = await _currentUser.GetRequiredUserNameAsync();
        var ownerSegment = UserStoragePath.Segment(owner);
        var directory = Path.Combine(UploadsRoot, ownerSegment, projectId);
        EnsureLegacyProjectDirectory(owner, projectId);
        return (directory, $"/uploads/{ownerSegment}/{projectId}/");
    }

    private static void EnsureLegacyProjectDirectory(string owner, string projectId)
    {
        if (!string.Equals(owner, "snet", StringComparison.OrdinalIgnoreCase)) { return; }
        var legacy = Path.Combine(UploadsRoot, projectId);
        var directory = Path.Combine(UploadsRoot, UserStoragePath.Segment(owner), projectId);
        if (!Directory.Exists(legacy) || Directory.Exists(directory)) { return; }
        Directory.CreateDirectory(Path.GetDirectoryName(directory)!);
        Directory.Move(legacy, directory);
    }

    private static string CacheKey(string owner, string projectId) => owner + "\n" + projectId;

    private static readonly JsonSerializerOptions TaskJsonOpts = new() { DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull };
    private static string SerializeTask(object task) => JsonSerializer.Serialize(task, TaskJsonOpts);
    private static AnnotationTask DeserializeTask(TaskData row)
    {
        try
        {
            return JsonSerializer.Deserialize<AnnotationTask>(row.dataJson, TaskJsonOpts)
                ?? throw new JsonException("The task payload is null.");
        }
        catch (Exception exception) when (exception is JsonException or NotSupportedException)
        {
            throw new InvalidDataException($"Task row {row.id} at index {row.taskIndex} contains invalid JSON and was not loaded.", exception);
        }
    }
}
