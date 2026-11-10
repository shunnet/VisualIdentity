namespace Snet.Yolo.Tasks.Services;

/// <summary>训练指标页的一次文件下载：已打开的只读流、建议文件名和内容类型；由响应负责释放流。</summary>
/// <param name="Stream">已打开的文件流。</param>
/// <param name="FileName">浏览器保存时使用的文件名。</param>
/// <param name="ContentType">内容类型。</param>
public sealed record TrainingDownload(Stream Stream, string FileName, string ContentType);
