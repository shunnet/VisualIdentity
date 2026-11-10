namespace Snet.Yolo.Tasks.Services;

/// <summary>后台视频识别完成后需要写回页面状态的结果。</summary>
public sealed record VideoRecognitionResult(string ResultJson, IReadOnlyList<ValidationDetection> Detections, string ResultUrl, double LastFrameRunTimeMilliseconds);
