namespace Snet.Yolo.Tasks.Core.Serialization.Export;

/// <summary>单个导出文件（相对路径 + 字节内容）。</summary>
public sealed record ExportFile(string Path, byte[] Content);
