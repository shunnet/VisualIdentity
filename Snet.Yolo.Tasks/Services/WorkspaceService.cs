namespace Snet.Yolo.Tasks.Services;

using Snet.Yolo.Server;
using Snet.Yolo.Server.models.data;
using Snet.Yolo.Tasks.Core.Workspace;
using Snet.Yolo.Tasks.Core.Models;
using System.Text.Json;

/// <summary>
/// 工程工作区服务：持久化迁移到 Snet.Yolo.Server（规范化 Project/Task 表）。
/// </summary>
public sealed class WorkspaceService
{
    private readonly ProjectOperate _projects;
    private readonly ProjectTaskOperate _tasks;

    public WorkspaceService(ProjectOperate projects, ProjectTaskOperate tasks)
    {
        _projects = projects;
        _tasks = tasks;
    }

    public async Task<List<WorkspaceProject>> ListProjectsAsync(CancellationToken ct = default)
    {
        var q = await _projects.QueryAsync(ct);
        if (!q.GetDetails(out List<ProjectData>? list)) { return new(); }
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
                .Select(t => DeserializeTask(t.dataJson)).Where(x => x is not null).Cast<AnnotationTask>()!.ToList();
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
            await _projects.UpdateAsync(pd, ct);
        }
        else
        {
            var add = await _projects.AddAsync(pd, ct);            add.GetDetails(out dynamic? d); // capture id not trivial via Snet.DB; re-query by projectId
            var after = await _projects.QueryAsync(project.Id, ct);
            if (after.GetDetails(out List<ProjectData>? list) && list is { Count: > 0 }) { pd.id = list[0].id; }
        }
        PopulateTasks(pd.id, project, ct);
    }

    private async Task PopulateTasks(int projectId, WorkspaceProject project, CancellationToken ct)
    {
        // 批量：一次按工程删除 + 一次批量插入（2 次调用，避免 N+1 慢）
        await _tasks.DeleteTasksByProjectAsync(projectId, ct);
        var rows = new List<TaskData>();
        var i = 0;
        foreach (var task in project.Tasks)
        {
            rows.Add(new TaskData { projectId = projectId, taskIndex = i++, dataJson = SerializeTask(task) });
        }
        await _tasks.SaveTasksAsync(rows, ct);
    }

    public Task DeleteProjectAsync(string projectId, CancellationToken ct = default) => _projects.DeleteAsync(projectId, ct);

    private static readonly JsonSerializerOptions TaskJsonOpts = new() { DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull };
    private static string SerializeTask(object task) => JsonSerializer.Serialize(task, TaskJsonOpts);
    private static AnnotationTask? DeserializeTask(string json) { try { return JsonSerializer.Deserialize<Snet.Yolo.Tasks.Core.Models.AnnotationTask>(json, TaskJsonOpts); } catch { return null; } }
}
