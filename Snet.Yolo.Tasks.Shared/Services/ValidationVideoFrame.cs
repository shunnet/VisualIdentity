namespace Snet.Yolo.Tasks.Services;

/// <summary>视频中单个采样帧的时间点及完整识别结果。</summary>
public sealed record ValidationVideoFrame(double TimeSeconds, string ResultJson);
