namespace Snet.Yolo.Tasks.Core.Serialization.Import;

/// <summary>一个已完成自检、等待落盘的 YOLO 图片条目。</summary>
/// <param name="ImageEntryPath">图片在 ZIP 中的规范相对路径。</param>
/// <param name="OriginalFileName">图片原始文件名。</param>
/// <param name="Boxes">与图片对应的标注框。</param>
public sealed record YoloImportImage(string ImageEntryPath, string OriginalFileName, IReadOnlyList<YoloImportBox> Boxes);
