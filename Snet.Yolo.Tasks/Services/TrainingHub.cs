using Microsoft.AspNetCore.SignalR;
using Snet.Yolo.Tasks.Core.Training;

namespace Snet.Yolo.Tasks.Services;

/// <summary>训练实时进度/日志推送。</summary>
public sealed class TrainingHub : Hub
{
    public Task Subscribe(string projectId) => Groups.AddToGroupAsync(Context.ConnectionId, projectId);
    public Task Unsubscribe(string projectId) => Groups.RemoveFromGroupAsync(Context.ConnectionId, projectId);

    public static async Task PushStatus(IHubContext<TrainingHub> hub, string projectId, TrainingStatus status)
        => await hub.Clients.Group(projectId).SendAsync("Status", status);
    public static async Task PushLog(IHubContext<TrainingHub> hub, string projectId, string line, string level)
        => await hub.Clients.Group(projectId).SendAsync("Log", new { projectId, line, level, at = DateTime.UtcNow });
}
