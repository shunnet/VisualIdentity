namespace Snet.Yolo.Tasks.Services;

/// <summary>
/// 单条验证检测结果的进程内快照：标签、置信度文本、坐标文本。
/// 视频结果按标签聚合，<c>Count</c> 是该标签在整段视频里的总识别次数（照片恒为 1，界面不展示）。
/// </summary>
public sealed record ValidationDetection(string Name, string Confidence, string Position, int Count = 1);
