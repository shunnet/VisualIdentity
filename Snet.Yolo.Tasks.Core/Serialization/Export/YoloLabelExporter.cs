using Snet.Yolo.Tasks.Core.Config;
using Snet.Yolo.Tasks.Core.Editing;
using Snet.Yolo.Tasks.Core.Models;
using System.Text;
using System.Text.Json.Nodes;

namespace Snet.Yolo.Tasks.Core.Serialization.Export;

/// <summary>按 YOLO 任务类型生成标签行（detect=bbox、obb=bbox+angle、segment=多边形点列、pose=关键点）。坐标统一 0-1 归一化。</summary>
public static class YoloLabelExporter
{
    /// <summary>返回与导出任务类型一致的官方模型名称。</summary>
    public static string BuildModelName(YoloTaskType task, string baseModel) => YoloTaskRegistry.ModelFor(task, baseModel);

    /// <summary>为单个任务生成多行标签文本（\n 结尾）。</summary>
    /// <param name="task">待导出的标注任务。</param>
    /// <param name="taskType">Ultralytics 任务类型。</param>
    /// <param name="classes">按数据集索引排序的对象类别。</param>
    /// <param name="keyPointNames">姿态任务的固定关键点顺序；其他任务可为空。</param>
    /// <returns>符合 Ultralytics 数据集格式的标签文本。</returns>
    public static string Build(
        AnnotationTask task,
        YoloTaskType taskType,
        IReadOnlyList<string> classes,
        IReadOnlyList<string>? keyPointNames = null)
    {
        var ann = task.Annotations.FirstOrDefault(a => a.WasCancelled != true);
        if (ann is null) { return string.Empty; }
        var labelField = LabelField(taskType);
        if (taskType == YoloTaskType.Pose) { return BuildPose(ann, classes, keyPointNames); }
        var sb = new StringBuilder();
        foreach (var row in ann.Result)
        {
            if (row.Type is RegionType.Labels or RegionType.Choices or RegionType.TextArea) { continue; }
            var rowLabelField = row.Type == RegionType.BrushLabels ? "brushlabels" : labelField;
            var label = row.Value is null ? null : ValueAccess.GetStringList(row.Value, rowLabelField).FirstOrDefault();
            if (label is null) { label = row.Value is null ? null : ValueAccess.GetStringList(row.Value, "labels").FirstOrDefault(); }
            if (label is null) { continue; }
            var idx = IndexOf(classes, label);
            if (idx < 0) { continue; }
            var line = BuildLine(row, idx, taskType);
            if (line is not null) { sb.AppendLine(line); }
        }
        return sb.ToString();
    }

    private static string? BuildLine(ResultRow row, int idx, YoloTaskType taskType)
    {
        if (row.Value is null) { return null; }
        // bbox 系（detect / obb）
        if (taskType is YoloTaskType.Detect or YoloTaskType.Obb)
        {
            if (row.Type != RegionType.RectangleLabels) { return null; }
            var x = ValueAccess.GetDouble(row.Value, "x");
            var y = ValueAccess.GetDouble(row.Value, "y");
            var w = ValueAccess.GetDouble(row.Value, "width");
            var h = ValueAccess.GetDouble(row.Value, "height");
            var cx = (x + w / 2d) / 100d;
            var cy = (y + h / 2d) / 100d;
            var ww = w / 100d; var hh = h / 100d;
            if (taskType == YoloTaskType.Obb)
            {
                var rotationRad = ValueAccess.GetDouble(row.Value, "rotation") * Math.PI / 180d;
                var corners = new[] { (0d, 0d), (w, 0d), (w, h), (0d, h) }
                    .Select(point => Rotate(point.Item1, point.Item2, rotationRad))
                    .Select(point => (X: (x + point.X) / 100d, Y: (y + point.Y) / 100d));
                return idx + " " + string.Join(" ", corners.SelectMany(point => new[] { F(point.X), F(point.Y) }));
            }
            return $"{idx} {F(cx)} {F(cy)} {F(ww)} {F(hh)}";
        }
        // segment
        if (taskType == YoloTaskType.Segment)
        {
            if (row.Type is not RegionType.PolygonLabels and not RegionType.BrushLabels) { return null; }
            var points = row.Type == RegionType.PolygonLabels
                ? ReadPolygonPoints(row.Value)
                : ReadBrushHull(row);
            if (points.Count < 3) { return null; }
            var sb = new StringBuilder();
            sb.Append(idx).Append(' ');
            foreach (var point in points)
            {
                sb.Append(F(point.X)).Append(' ').Append(F(point.Y)).Append(' ');
            }
            return sb.ToString().TrimEnd();
        }
        return null;
    }

    /// <summary>按父矩形分组导出 Ultralytics Pose 行。</summary>
    private static string BuildPose(Models.Annotation ann, IReadOnlyList<string> classes, IReadOnlyList<string>? configuredKeyPoints)
    {
        var rectangles = ann.Result
            .Where(row => row.Type == RegionType.RectangleLabels && !string.IsNullOrEmpty(row.Id) && row.Value is not null)
            .ToDictionary(row => row.Id!, StringComparer.Ordinal);
        var keyPointNames = configuredKeyPoints is { Count: > 0 }
            ? configuredKeyPoints
            : ann.Result.Where(row => row.Type == RegionType.KeyPointLabels && row.Value is not null)
                .Select(row => ValueAccess.GetStringList(row.Value!, "keypointlabels").FirstOrDefault())
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .Distinct(StringComparer.Ordinal)
                .Cast<string>()
                .ToArray();
        var sb = new StringBuilder();
        foreach (var group in ann.Result
                     .Where(row => row.Type == RegionType.KeyPointLabels && row.Value is not null && !string.IsNullOrEmpty(row.ParentId))
                     .GroupBy(row => row.ParentId!, StringComparer.Ordinal))
        {
            if (!rectangles.TryGetValue(group.Key, out var rectangle)) { continue; }
            var className = ValueAccess.GetStringList(rectangle.Value!, "rectanglelabels").FirstOrDefault();
            var classIndex = className is null ? -1 : IndexOf(classes, className);
            if (classIndex < 0) { continue; }
            var value = rectangle.Value!;
            var x = ValueAccess.GetDouble(value, "x") / 100d;
            var y = ValueAccess.GetDouble(value, "y") / 100d;
            var width = ValueAccess.GetDouble(value, "width") / 100d;
            var height = ValueAccess.GetDouble(value, "height") / 100d;
            sb.Append(classIndex).Append(' ')
              .Append(F(x + width / 2d)).Append(' ').Append(F(y + height / 2d)).Append(' ')
              .Append(F(width)).Append(' ').Append(F(height));
            var rowsByName = group
                .Select(row => (Name: ValueAccess.GetStringList(row.Value!, "keypointlabels").FirstOrDefault(), Row: row))
                .Where(item => !string.IsNullOrWhiteSpace(item.Name))
                .GroupBy(item => item.Name!, StringComparer.Ordinal)
                .ToDictionary(items => items.Key, items => items.First().Row, StringComparer.Ordinal);
            foreach (var keyPointName in keyPointNames)
            {
                if (!rowsByName.TryGetValue(keyPointName, out var row))
                {
                    sb.Append(" 0 0 0");
                    continue;
                }
                sb.Append(' ').Append(F(ValueAccess.GetDouble(row.Value!, "x") / 100d))
                  .Append(' ').Append(F(ValueAccess.GetDouble(row.Value!, "y") / 100d)).Append(" 2");
            }
            sb.AppendLine();
        }
        return sb.ToString();
    }

    private static (double X, double Y) Rotate(double x, double y, double angle)
        => (x * Math.Cos(angle) - y * Math.Sin(angle), x * Math.Sin(angle) + y * Math.Cos(angle));

    private static IReadOnlyList<Point> ReadPolygonPoints(JsonObject value)
    {
        if (value["points"] is not JsonArray points) { return Array.Empty<Point>(); }
        var result = new List<Point>(points.Count);
        foreach (var node in points)
        {
            if (node is not JsonArray pair || pair.Count < 2) { continue; }
            result.Add(new Point(
                Math.Clamp(ReadNumber(pair[0]), 0d, 100d) / 100d,
                Math.Clamp(ReadNumber(pair[1]), 0d, 100d) / 100d));
        }
        return result;
    }

    private static IReadOnlyList<Point> ReadBrushHull(ResultRow row)
    {
        var width = (int)(row.OriginalWidth ?? 0);
        var height = (int)(row.OriginalHeight ?? 0);
        if (width <= 0 || height <= 0 || row.Value?["rle"] is not JsonArray encoded) { return Array.Empty<Point>(); }
        var rle = encoded.Select(node => (byte)Math.Clamp(node?.GetValue<int>() ?? 0, 0, byte.MaxValue)).ToArray();
        var mask = RleCodec.Decode(rle, checked(width * height));
        var boundary = new List<Point>();
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                if (mask[y * width + x] == 0) { continue; }
                if (x > 0 && x + 1 < width && y > 0 && y + 1 < height &&
                    mask[y * width + x - 1] != 0 && mask[y * width + x + 1] != 0 &&
                    mask[(y - 1) * width + x] != 0 && mask[(y + 1) * width + x] != 0) { continue; }
                boundary.Add(new Point(x / (double)width, y / (double)height));
            }
        }
        return ConvexHull(boundary);
    }

    private static IReadOnlyList<Point> ConvexHull(List<Point> points)
    {
        if (points.Count <= 3) { return points; }
        var sorted = points.Distinct().OrderBy(point => point.X).ThenBy(point => point.Y).ToArray();
        if (sorted.Length <= 3) { return sorted; }
        var hull = new List<Point>(sorted.Length * 2);
        foreach (var point in sorted)
        {
            while (hull.Count >= 2 && Cross(hull[^2], hull[^1], point) <= 0d) { hull.RemoveAt(hull.Count - 1); }
            hull.Add(point);
        }
        var lowerCount = hull.Count;
        for (var index = sorted.Length - 2; index >= 0; index--)
        {
            var point = sorted[index];
            while (hull.Count > lowerCount && Cross(hull[^2], hull[^1], point) <= 0d) { hull.RemoveAt(hull.Count - 1); }
            hull.Add(point);
        }
        hull.RemoveAt(hull.Count - 1);
        return hull;
    }

    private static double Cross(Point origin, Point first, Point second)
        => (first.X - origin.X) * (second.Y - origin.Y) - (first.Y - origin.Y) * (second.X - origin.X);

    private static double ReadNumber(JsonNode? node)
    {
        if (node is not JsonValue value) { return 0d; }
        if (value.TryGetValue<double>(out var doubleValue)) { return doubleValue; }
        if (value.TryGetValue<int>(out var intValue)) { return intValue; }
        if (value.TryGetValue<long>(out var longValue)) { return longValue; }
        if (value.TryGetValue<decimal>(out var decimalValue)) { return (double)decimalValue; }
        return 0d;
    }

    private readonly record struct Point(double X, double Y);

    private static string LabelField(YoloTaskType taskType) => taskType switch
    {
        YoloTaskType.Segment => "polygonlabels",
        YoloTaskType.Pose => "keypointlabels",
        _ => "rectanglelabels",
    };

    private static int IndexOf(IReadOnlyList<string> classes, string label)
    {
        for (var i = 0; i < classes.Count; i++) { if (string.Equals(classes[i], label, StringComparison.Ordinal)) { return i; } }
        return -1;
    }

    private static string F(double v) => v.ToString("0.#####", System.Globalization.CultureInfo.InvariantCulture);
}
