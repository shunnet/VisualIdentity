using Microsoft.AspNetCore.SignalR;
using Snet.Yolo.Server;
using Snet.Yolo.Tasks.Core.Training;

namespace Snet.Yolo.Tasks.Services;

/// <summary>训练实时进度/日志推送。</summary>
public sealed class TrainingHub(ProjectOperate projects) : Hub
{
    public async Task Subscribe(string projectId)
    {
        var owner = Context.User?.Identity?.Name;
        if (string.IsNullOrWhiteSpace(owner)) { throw new HubException("未登录。"); }
        var result = await projects.QueryAsync(owner, projectId, Context.ConnectionAborted);
        if (!result.Status) { throw new HubException("工程不存在或无权访问。"); }
        await Groups.AddToGroupAsync(Context.ConnectionId, Group(owner, projectId));
    }
    public Task Unsubscribe(string projectId)
    {
        var owner = Context.User?.Identity?.Name ?? string.Empty;
        return Groups.RemoveFromGroupAsync(Context.ConnectionId, Group(owner, projectId));
    }

    public static async Task PushStatus(IHubContext<TrainingHub> hub, string owner, string projectId, TrainingStatus status)
        => await hub.Clients.Group(Group(owner, projectId)).SendAsync("Status", status);
    public static async Task PushLog(IHubContext<TrainingHub> hub, string owner, string projectId, string line, string level)
        => await hub.Clients.Group(Group(owner, projectId)).SendAsync("Log", new { projectId, line, level, at = DateTime.UtcNow });

    private static string Group(string owner, string projectId) => owner + "\n" + projectId;
}
