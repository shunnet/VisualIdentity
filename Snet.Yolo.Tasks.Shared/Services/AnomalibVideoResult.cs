namespace Snet.Yolo.Tasks.Services;

/// <summary>已生成的标注视频及异常帧统计。</summary>
/// <param name="FileName">写入用户上传目录的结果视频文件名。</param>
/// <param name="TotalFrames">已识别的视频帧数。</param>
/// <param name="AnomalousFrames">被模型判定异常的视频帧数。</param>
public sealed record AnomalibVideoResult(string FileName, int TotalFrames, int AnomalousFrames);
