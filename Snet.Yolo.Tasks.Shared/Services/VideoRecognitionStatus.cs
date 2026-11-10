namespace Snet.Yolo.Tasks.Services;

/// <summary>供验证页面读取的视频任务不可变状态快照。</summary>
public sealed record VideoRecognitionStatus(
    VideoRecognitionStage Stage,
    int TotalFrames,
    int CompletedFrames,
    double FramesPerSecond,
    TimeSpan Elapsed,
    string? Error,
    double? LastFrameRunTimeMilliseconds)
{
    /// <summary>指示后台任务是否已经结束（含被用户取消）。</summary>
    public bool IsTerminal => Stage is VideoRecognitionStage.Completed or VideoRecognitionStage.Cancelled or VideoRecognitionStage.Failed;
}
