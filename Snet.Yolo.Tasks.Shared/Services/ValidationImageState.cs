namespace Snet.Yolo.Tasks.Services;

/// <summary>与指定模型关联的验证文件及其最后一次识别结果。</summary>
public sealed record ValidationImageState(
    Guid Id,
    string Name,
    string Url,
    string? ResultJson,
    IReadOnlyList<ValidationDetection> Detections,
    bool IsVideo = false,
    string ContentType = "",
    IReadOnlyList<ValidationVideoFrame>? VideoFrames = null,
    string? ResultUrl = null,
    int Width = 0,
    int Height = 0);
