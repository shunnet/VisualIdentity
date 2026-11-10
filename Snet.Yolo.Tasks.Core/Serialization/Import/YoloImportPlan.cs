namespace Snet.Yolo.Tasks.Core.Serialization.Import;

/// <summary>YOLO ZIP 自检结果。</summary>
/// <param name="Classes">按类别索引排序的标签名称。</param>
/// <param name="Images">通过目录和标注检查的图片。</param>
/// <param name="UnlabeledImageCount">没有任何标注框的图片数量（含缺少标注文件的图片）。</param>
public sealed record YoloImportPlan(IReadOnlyList<string> Classes, IReadOnlyList<YoloImportImage> Images, int UnlabeledImageCount = 0)
{
    /// <summary>包内标注框总数。</summary>
    public int AnnotationCount => Images.Sum(image => image.Boxes.Count);
}
