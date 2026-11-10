using Snet.Yolo.Server.anomalib;

namespace Snet.Yolo.Tasks.Services;

/// <summary>与上传文件绑定的 Anomalib 验证结果。</summary>
public sealed record AnomalibValidationMedia
{
    public required string Path { get; init; }
    public required string Url { get; init; }
    public required string Name { get; init; }
    public required bool IsVideo { get; init; }
    public AnomalibInferenceOutput? Output { get; set; }
    public string? VideoResultUrl { get; set; }
    public string ResultMessageKey { get; set; } = string.Empty;
    public object[] ResultMessageArguments { get; set; } = [];
}
