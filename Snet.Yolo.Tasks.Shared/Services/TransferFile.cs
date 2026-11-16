namespace Snet.Yolo.Tasks.Services;

/// <summary>持久上传文件的不可变元数据；文件名仅用于显示和业务格式校验。</summary>
/// <param name="Name">原始文件名。</param>
/// <param name="Size">声明的字节总量。</param>
public sealed record TransferFile(string Name, long Size);
