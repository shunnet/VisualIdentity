namespace Snet.Yolo.Tasks.Services;

/// <summary>视频识别当前进度。</summary>
/// <param name="Stage">当前视频处理阶段。</param>
/// <param name="CompletedFrames">已完成识别的帧数。</param>
/// <param name="TotalFrames">本次视频帧总数。</param>
public sealed record AnomalibVideoProgress(AnomalibVideoStage Stage, int CompletedFrames, int TotalFrames);
