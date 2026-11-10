namespace Snet.Yolo.Tasks.Services;

/// <summary>当前用户的模型选择、文件、结果、日志和区域过滤设置。</summary>
public sealed record AnomalibValidationSnapshot(
    string SelectedModelKey, string? SelectedFileUrl,
    IReadOnlyList<AnomalibValidationMedia> Files, IReadOnlyList<AnomalibValidationLog> Logs,
    float PixelThreshold, int MinimumArea);
