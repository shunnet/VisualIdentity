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
    /// <summary>持久上传的来源阶段；后台业务导入和旧流式调用为空。</summary>
    public string? TransferStage { get; init; }
    /// <summary>当前等待用户操作或重试，界面不应显示正在网络传输的动画。</summary>
    public bool NeedsSourceAction => TransferStage is "awaiting-source" or "paused";
    /// <summary>页内进度与顶部指示共用的短阶段文案。</summary>
    public string? StageResourceKey => TransferStage switch
    {
        "preparing" => "UploadPreparingSource", "awaiting-source" => "UploadWaitingSource", "paused" => "UploadTransferPaused", _ => null,
    };
}
