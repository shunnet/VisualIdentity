using Snet.Yolo.Tasks.Core.Models;
using System.Text.Json.Nodes;

namespace Snet.Yolo.Tasks.Core.Geometry;

/// <summary>闭合多边形的贝塞尔边：每条边对应起点索引，控制点以百分比保存。</summary>
public static class PolygonPath
{
    /// <summary>标注 value 内保留原始曲线的扩展字段。</summary>
    public const string CurvesField = "snet_bezier";
    /// <summary>图像像素坐标。</summary>
    /// <param name="X">横坐标。</param>
    /// <param name="Y">纵坐标。</param>
    public readonly record struct Point(double X, double Y);

    /// <summary>读取像素控制点；null 表示直线，四个数依次为两控制点的 x/y。</summary>
    public static double[]?[] ReadCurves(JsonObject value, int count, double width, double height)
    {
        var result = new double[]?[count];
        if (value[CurvesField] is not JsonArray curves) { return result; }
        if (curves.Count != count) { throw new InvalidDataException("多边形曲线与顶点数量不一致。"); }
        for (var i = 0; i < count; i++)
        {
            if (curves[i] is null) { continue; }
            if (curves[i] is not JsonArray c || c.Count != 4) { throw new InvalidDataException("多边形曲线控制点格式错误。"); }
            result[i] = [Number(c[0]) * width / 100, Number(c[1]) * height / 100,
                Number(c[2]) * width / 100, Number(c[3]) * height / 100];
        }
        return result;
    }

    /// <summary>生成像素误差不超过 0.5px 的闭合路径顶点，不重复末尾起点。直线保持原顶点。</summary>
    public static IReadOnlyList<Point> Flatten(ResultRow row, double tolerance = 0.5)
    {
        if (!double.IsFinite(tolerance) || tolerance <= 0) { throw new ArgumentOutOfRangeException(nameof(tolerance)); }
        if (row.Value?["points"] is not JsonArray points || points.Count < 3) { return Array.Empty<Point>(); }
        var width = row.OriginalWidth is > 0 ? row.OriginalWidth.Value : 100;
        var height = row.OriginalHeight is > 0 ? row.OriginalHeight.Value : 100;
        var anchors = points.Select(p => p is JsonArray pair && pair.Count >= 2
            ? new Point(Number(pair[0]) * width / 100, Number(pair[1]) * height / 100)
            : throw new InvalidDataException("多边形顶点格式错误。")).ToArray();
        var curves = ReadCurves(row.Value, anchors.Length, width, height);
        var result = new List<Point> { anchors[0] };
        for (var i = 0; i < anchors.Length; i++)
        {
            var next = anchors[(i + 1) % anchors.Length];
            if (curves[i] is { } c) { Subdivide(anchors[i], new(c[0], c[1]), new(c[2], c[3]), next, tolerance * tolerance, 0, result); }
            else { result.Add(next); }
        }
        result.RemoveAt(result.Count - 1);
        return result;
    }

    private static void Subdivide(Point a, Point b, Point c, Point d, double toleranceSquared, int depth, List<Point> result)
    {
        if (result.Count >= 65_536) { throw new InvalidDataException("曲线导出顶点过多，请简化标注。"); }
        if (Math.Max(DistanceSquared(b, a, d), DistanceSquared(c, a, d)) <= toleranceSquared)
        { result.Add(d); return; }
        if (depth >= 16) { throw new InvalidDataException("曲线无法在指定精度内导出。"); }
        var ab = Mid(a, b); var bc = Mid(b, c); var cd = Mid(c, d);
        var abc = Mid(ab, bc); var bcd = Mid(bc, cd); var middle = Mid(abc, bcd);
        Subdivide(a, ab, abc, middle, toleranceSquared, depth + 1, result);
        Subdivide(middle, bcd, cd, d, toleranceSquared, depth + 1, result);
    }

    private static Point Mid(Point a, Point b) => new((a.X + b.X) / 2, (a.Y + b.Y) / 2);
    private static double DistanceSquared(Point p, Point a, Point b)
    {
        var dx = b.X - a.X; var dy = b.Y - a.Y;
        var length = dx * dx + dy * dy;
        var t = length == 0 ? 0 : Math.Clamp(((p.X - a.X) * dx + (p.Y - a.Y) * dy) / length, 0, 1);
        return Math.Pow(p.X - a.X - t * dx, 2) + Math.Pow(p.Y - a.Y - t * dy, 2);
    }
    private static double Number(JsonNode? node)
    {
        if (node is not JsonValue v) { throw new InvalidDataException("多边形坐标必须为数值。"); }
        var n = v.TryGetValue<double>(out var d) ? d : v.TryGetValue<int>(out var i) ? i
            : v.TryGetValue<long>(out var l) ? l : v.TryGetValue<decimal>(out var m) ? (double)m : double.NaN;
        if (!double.IsFinite(n) || n < 0 || n > 100)
        { throw new InvalidDataException("多边形坐标必须在 0–100 范围内。"); }
        return n;
    }
}
