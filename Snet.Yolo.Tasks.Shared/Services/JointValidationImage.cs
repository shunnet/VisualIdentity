using Snet.Yolo.Server.anomalib;

namespace Snet.Yolo.Tasks.Services;

/// <summary>联合验证图片及其独立识别结果；原图用于推理，缩略图仅用于列表展示。</summary>
public sealed record JointValidationImage(
    string Id, string Path, string Url, string Name, int Width, int Height,
    string PreviewUrl, JointValidationOutput? Output = null, string? Error = null);
