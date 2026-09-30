using SkiaSharp;

namespace Snet.Yolo.Server.sam;

/// <summary>提取提示点所在的连通物体，保留实心掩码及孔洞，并生成简化外轮廓。</summary>
public static class SamMaskGeometry
{
    /// <summary>输入工作尺寸的 0/255 掩码，返回原图坐标几何；不使用会吞掉凹边的凸包。</summary>
    public static SamResult Create(byte[] source, int width, int height, int originalWidth, int originalHeight, int seedX, int seedY, float score, CancellationToken token = default)
    {
        if (width <= 0 || height <= 0 || source.Length != checked(width * height) || originalWidth <= 0 || originalHeight <= 0 || (long)originalWidth * originalHeight > 24_000_000
            || seedX < 0 || seedY < 0 || seedX >= width || seedY >= height || source[seedY * width + seedX] == 0)
        { throw new ArgumentException("SAM 掩码尺寸或前景点无效。"); }
        var component = new byte[source.Length]; var queue = new int[source.Length]; var head = 0; var tail = 1;
        queue[0] = seedY * width + seedX; component[queue[0]] = 255;
        var minX = width; var minY = height; var maxX = 0; var maxY = 0;
        while (head < tail)
        {
            if ((head & 4095) == 0) { token.ThrowIfCancellationRequested(); }
            var i = queue[head++]; var x = i % width; var y = i / width;
            minX = Math.Min(minX, x); maxX = Math.Max(maxX, x); minY = Math.Min(minY, y); maxY = Math.Max(maxY, y);
            if (x > 0) { Add(i - 1); }
            if (x + 1 < width) { Add(i + 1); }
            if (y > 0) { Add(i - width); }
            if (y + 1 < height) { Add(i + width); }
        }
        void Add(int i) { if (source[i] != 0 && component[i] == 0) { component[i] = 255; queue[tail++] = i; } }
        // 有向像素边界；每条边只访问一次。外环面积为正，内孔为负。
        var edges = new Dictionary<int, List<int>>(); var stride = width + 1;
        void Edge(int ax, int ay, int bx, int by)
        { var a = ay * stride + ax; if (!edges.TryGetValue(a, out var list)) { edges[a] = list = []; } list.Add(by * stride + bx); }
        for (var y = minY; y <= maxY; y++)
        {
            token.ThrowIfCancellationRequested();
            for (var x = minX; x <= maxX; x++)
            {
                var i = y * width + x; if (component[i] == 0) { continue; }
                if (y == 0 || component[i - width] == 0) { Edge(x, y, x + 1, y); }
                if (x == width - 1 || component[i + 1] == 0) { Edge(x + 1, y, x + 1, y + 1); }
                if (y == height - 1 || component[i + width] == 0) { Edge(x + 1, y + 1, x, y + 1); }
                if (x == 0 || component[i - 1] == 0) { Edge(x, y + 1, x, y); }
            }
        }
        List<(double X, double Y)> outline = []; double largest = 0;
        while (edges.Count > 0)
        {
            token.ThrowIfCancellationRequested();
            var start = edges.First().Key; var current = start; var ring = new List<(double X, double Y)>();
            do
            {
                ring.Add((current % stride, current / stride));
                if (!edges.TryGetValue(current, out var list)) { throw new InvalidDataException("SAM 轮廓不闭合。"); }
                var next = list[^1]; list.RemoveAt(list.Count - 1); if (list.Count == 0) { edges.Remove(current); }
                current = next;
            } while (current != start);
            double area = 0;
            for (var i = 0; i < ring.Count; i++) { var a = ring[i]; var b = ring[(i + 1) % ring.Count]; area += a.X * b.Y - b.X * a.Y; }
            if (area > largest) { largest = area; outline = ring; }
        }
        if (outline.Count < 3) { throw new InvalidDataException("SAM 没有可用外轮廓。"); }
        // 循环轮廓拆成两段，迭代 RDP 避免递归栈溢出。
        var pivot = outline.Count / 2; var first = outline.Take(pivot + 1).ToList(); var second = outline.Skip(pivot).Append(outline[0]).ToList();
        var simplified = Simplify(first, 1).Concat(Simplify(second, 1).Skip(1).SkipLast(1)).ToList();
        if (simplified.Count < 3) { simplified = outline; }
        if (simplified.Count > 4096) { throw new InvalidDataException("SAM 轮廓过于复杂，请换一个提示点。"); }
        var sx = originalWidth / (double)width; var sy = originalHeight / (double)height;
        var mask = new byte[checked(originalWidth * originalHeight)];
        for (var y = 0; y < originalHeight; y++)
        { token.ThrowIfCancellationRequested(); var row = Math.Min(height - 1, (int)(y / sy)) * width; for (var x = 0; x < originalWidth; x++) { mask[y * originalWidth + x] = component[row + Math.Min(width - 1, (int)(x / sx))]; } }
        using var bitmap = new SKBitmap(width, height);
        for (var y = 0; y < height; y++) { for (var x = 0; x < width; x++) { bitmap.SetPixel(x, y, component[y * width + x] == 0 ? SKColors.Transparent : new SKColor(64, 160, 255, 255)); } }
        using var image = SKImage.FromBitmap(bitmap); using var png = image.Encode(SKEncodedImageFormat.Png, 100);
        return new SamResult
        {
            Width = originalWidth,
            Height = originalHeight,
            Mask = mask,
            PointsX = simplified.Select(p => p.X * sx).ToArray(),
            PointsY = simplified.Select(p => p.Y * sy).ToArray(),
            X = minX * sx,
            Y = minY * sy,
            BoxWidth = (maxX + 1 - minX) * sx,
            BoxHeight = (maxY + 1 - minY) * sy,
            Score = score,
            PreviewDataUrl = "data:image/png;base64," + Convert.ToBase64String(png.ToArray())
        };
    }

    private static List<(double X, double Y)> Simplify(List<(double X, double Y)> points, double tolerance)
    {
        var keep = new bool[points.Count]; keep[0] = keep[^1] = true; var stack = new Stack<(int A, int B)>(); stack.Push((0, points.Count - 1));
        while (stack.TryPop(out var segment))
        {
            var a = points[segment.A]; var b = points[segment.B]; var dx = b.X - a.X; var dy = b.Y - a.Y; var length = dx * dx + dy * dy;
            var best = tolerance * tolerance; var index = -1;
            for (var i = segment.A + 1; i < segment.B; i++)
            { var p = points[i]; var t = length == 0 ? 0 : Math.Clamp(((p.X - a.X) * dx + (p.Y - a.Y) * dy) / length, 0, 1); var ex = p.X - a.X - t * dx; var ey = p.Y - a.Y - t * dy; var d = ex * ex + ey * ey; if (d > best) { best = d; index = i; } }
            if (index < 0) { continue; }
            keep[index] = true; stack.Push((segment.A, index)); stack.Push((index, segment.B));
        }
        return points.Where((_, i) => keep[i]).ToList();
    }
}
