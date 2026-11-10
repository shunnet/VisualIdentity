namespace Snet.Yolo.Tasks.Services;

/// <summary>上传任务状态的只读快照。</summary>
public sealed record UploadJob(
    string ScopeKey,
    UploadKind Kind,
    UploadPhase Phase,
    string Status,
    int Total,
    int Completed,
    int? Percent,
    string? Error,
    DateTime UpdatedAt)
{
    /// <summary>任务是否仍在进行。</summary>
    public bool IsRunning => Phase == UploadPhase.Running;
}
