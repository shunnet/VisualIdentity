namespace Snet.Yolo.Tasks.Services;

/// <summary>当前用户指定模型的文件、结果、日志、选中项和区域过滤设置。</summary>
public sealed record AnomalibValidationSnapshot(
    string SelectedModelKey, string? SelectedFileUrl,
    IReadOnlyList<AnomalibValidationMedia> Files, IReadOnlyList<AnomalibValidationLog> Logs,
    float PixelThreshold, int MinimumArea);
