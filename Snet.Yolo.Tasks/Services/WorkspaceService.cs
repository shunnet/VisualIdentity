namespace Snet.Yolo.Tasks.Services;

using Snet.Yolo.Tasks.Core.Workspace;
using Snet.Yolo.Tasks.Core.Stores;

/// <summary>
/// 工程工作区服务：每个工程独立存储为一行（key = 工程 Id），
/// 提供工程列表/读取/保存/删除（SQLite 后端）。
/// </summary>
public sealed class WorkspaceService
{
    private readonly IWorkspaceStore _store;

    /// <summary>构造。</summary>
    public WorkspaceService(IWorkspaceStore store)
    {
        _store = store;
    }

    /// <summary>按最后保存时间倒序列出工程。</summary>
    public async Task<List<WorkspaceProject>> ListProjectsAsync(CancellationToken cancellationToken = default)
    {
        var names = await _store.ListNamesAsync(cancellationToken);
        var projects = new List<WorkspaceProject>();
        foreach (var name in names)
        {
            var json = await _store.LoadAsync(name, cancellationToken);
            if (json is null)
            {
                continue;
            }

            var document = WorkspaceJson.Deserialize(json);
            var project = document?.Projects.FirstOrDefault();
            if (project is not null)
            {
                projects.Add(project);
            }
        }

        return projects.OrderByDescending(project => project.UpdatedAt).ToList();
    }

    /// <summary>读取指定工程。</summary>
    public async Task<WorkspaceProject?> GetProjectAsync(string projectId, CancellationToken cancellationToken = default)
    {
        var json = await _store.LoadAsync(projectId, cancellationToken);
        if (json is null)
        {
            return null;
        }

        return WorkspaceJson.Deserialize(json)?.Projects.FirstOrDefault();
    }

    /// <summary>保存工程（新建或覆盖）。</summary>
    public async Task SaveProjectAsync(WorkspaceProject project, CancellationToken cancellationToken = default)
    {
        project.UpdatedAt = DateTime.UtcNow;
        var document = new WorkspaceDocument();
        document.Projects.Add(project);
        await _store.SaveAsync(project.Id, WorkspaceJson.Serialize(document), cancellationToken);
    }

    /// <summary>删除工程。</summary>
    public Task DeleteProjectAsync(string projectId, CancellationToken cancellationToken = default)
        => _store.DeleteAsync(projectId, cancellationToken);
}
