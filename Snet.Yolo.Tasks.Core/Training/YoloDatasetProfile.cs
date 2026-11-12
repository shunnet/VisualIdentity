using Snet.Yolo.Tasks.Core.Config;
using Snet.Yolo.Tasks.Core.Editing;
using Snet.Yolo.Tasks.Core.Geometry;
using Snet.Yolo.Tasks.Core.Models;

namespace Snet.Yolo.Tasks.Core.Training;

/// <summary>
/// 训练前对标注数据的画像：图片数、标注框数、类别数、每个框相对原图的尺寸、图片分辨率。
/// 给 <see cref="YoloTrainingAdvisor"/> 算推荐值用；只读内存里的标注，不碰磁盘。
/// </summary>
/// <param name="ImageCount">有图片的任务数。</param>
/// <param name="AnnotatedImageCount">至少有一个可用标注的图片数（分类任务为已选分类的图片数）。</param>
/// <param name="BoxCount">可用于尺寸统计的标注框数（矩形框，或多边形的外接框）。</param>
/// <param name="ClassCount">类别数。</param>
/// <param name="Boxes">逐框的原图尺寸（仅带框任务）。</param>
/// <param name="MedianLongEdge">图片长边中位数（原图像素；未知为 0）。</param>
public sealed record YoloDatasetProfile(
    int ImageCount,
    int AnnotatedImageCount,
    int BoxCount,
    int ClassCount,
    IReadOnlyList<YoloBoxSample> Boxes,
    int MedianLongEdge)
{
    /// <summary>框短边占所在图片长边的比例的分位数；没有框时返回 0。</summary>
    /// <remarks>
    /// 训练时图片按长边等比缩放到输入尺寸，框在输入上的像素 = 这个比例 × 输入尺寸；
    /// 比例与输入尺寸无关，所以框的大小顺序对任何输入尺寸都一样，分位数只需要算一次。
    /// </remarks>
    /// <param name="quantile">分位数（0–1），例如 0.05 表示最小的那 5%。</param>
    public double ShortSideFractionQuantile(double quantile)
    {
        if (Boxes.Count == 0) { return 0; }
        var sorted = Boxes.Select(box => box.PixelsAt(1)).Order().ToArray();
        return sorted[Math.Clamp((int)Math.Round((sorted.Length - 1) * quantile), 0, sorted.Length - 1)];
    }

    /// <summary>某训练输入尺寸下框短边像素的分位数；没有框时返回 0。</summary>
    /// <param name="imageSize">训练输入边长。</param>
    /// <param name="quantile">分位数（0–1），例如 0.05。</param>
    public double BoxPixelsQuantile(int imageSize, double quantile) => ShortSideFractionQuantile(quantile) * imageSize;

    /// <summary>从项目的标注任务生成画像。</summary>
    /// <param name="tasks">项目的全部任务。</param>
    /// <param name="taskType">YOLO 任务类型。</param>
    /// <param name="classCount">类别数。</param>
    public static YoloDatasetProfile From(IEnumerable<AnnotationTask> tasks, YoloTaskType taskType, int classCount)
    {
        var imageCount = 0;
        var annotated = 0;
        var boxes = new List<YoloBoxSample>();
        var longEdges = new List<int>();
        foreach (var task in tasks)
        {
            if (string.IsNullOrEmpty(task.Data?["image"]?.ToString())) { continue; }
            imageCount++;
            var annotation = task.Annotations.FirstOrDefault(candidate => candidate.WasCancelled != true);
            if (annotation is null) { continue; }
            var used = false;
            var longEdge = 0;
            foreach (var row in annotation.Result)
            {
                if (taskType == YoloTaskType.Classify)
                {
                    used |= row.Type == RegionType.Choices;
                    continue;
                }
                if (row.Value is null || row.OriginalWidth is not > 0 || row.OriginalHeight is not > 0) { continue; }
                if (taskType == YoloTaskType.Segment && row.Type == RegionType.BrushLabels)
                {
                    // 笔刷标注导出时同样会变成实例，但没有现成的外接框：只计入已标注图片数，不参与目标尺寸统计。
                    used = true;
                    continue;
                }
                var shortSide = taskType == YoloTaskType.Segment ? PolygonShortSide(row) : RectangleShortSide(row);
                if (shortSide <= 0) { continue; }
                longEdge = (int)Math.Round(Math.Max(row.OriginalWidth.Value, row.OriginalHeight.Value));
                boxes.Add(new YoloBoxSample(shortSide, longEdge));
                used = true;
            }
            if (!used) { continue; }
            annotated++;
            if (longEdge > 0) { longEdges.Add(longEdge); }
        }
        // 图片长边的中位数按图片算，而不是按框算：一张图里框多不该让它说了算。
        var median = longEdges.Count == 0 ? 0 : longEdges.Order().ElementAt(longEdges.Count / 2);
        return new YoloDatasetProfile(imageCount, annotated, boxes.Count, classCount, boxes, median);
    }

    /// <summary>矩形框（检测、旋转框、姿态的父框）的短边，原图像素；框的宽高以图片宽高的百分比保存。</summary>
    private static double RectangleShortSide(ResultRow row)
    {
        if (row.Type != RegionType.RectangleLabels || row.Value is null) { return 0d; }
        var width = ValueAccess.GetDouble(row.Value, "width") * row.OriginalWidth!.Value / 100d;
        var height = ValueAccess.GetDouble(row.Value, "height") * row.OriginalHeight!.Value / 100d;
        return Math.Min(width, height);
    }

    /// <summary>多边形外接框的短边，原图像素。顶点格式不合法的行跳过（导出时会单独报错），不让画像计算失败。</summary>
    private static double PolygonShortSide(ResultRow row)
    {
        if (row.Type != RegionType.PolygonLabels) { return 0d; }
        IReadOnlyList<PolygonPath.Point> vertices;
        try { vertices = PolygonPath.Flatten(row); }
        catch (InvalidDataException) { return 0d; }
        if (vertices.Count < 3) { return 0d; }
        var width = vertices.Max(vertex => vertex.X) - vertices.Min(vertex => vertex.X);
        var height = vertices.Max(vertex => vertex.Y) - vertices.Min(vertex => vertex.Y);
        return Math.Min(width, height);
    }
}
