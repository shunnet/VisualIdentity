namespace Snet.Yolo.Tasks.Core.Serialization.Import;

/// <summary>YOLO 归一化矩形标注。</summary>
/// <param name="ClassIndex">类别索引。</param>
/// <param name="XCenter">中心点 X，范围 0 到 1。</param>
/// <param name="YCenter">中心点 Y，范围 0 到 1。</param>
/// <param name="Width">宽度，范围 0 到 1。</param>
/// <param name="Height">高度，范围 0 到 1。</param>
public sealed record YoloImportBox(int ClassIndex, double XCenter, double YCenter, double Width, double Height);
