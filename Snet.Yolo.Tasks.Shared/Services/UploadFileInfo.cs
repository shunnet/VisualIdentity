namespace Snet.Yolo.Tasks.Services;

/// <summary>浏览器文件的名称与大小快照（用于界面展示，避免页面缓存 IBrowserFile）。</summary>
public sealed record UploadFileInfo(string Name, long Size);
