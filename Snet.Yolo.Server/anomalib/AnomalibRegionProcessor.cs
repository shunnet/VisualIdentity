using System.Buffers;

namespace Snet.Yolo.Server.Anomalib;

/// <summary>
/// 异常掩码区域提取选项。
/// </summary>
public sealed class AnomalibRegionOptions
{
    /// <summary>获取或设置保留区域所需的最小异常像素数。</summary>
    public int MinimumArea { get; set; } = 1;

    /// <summary>获取或设置可合并区域之间允许的最大横纵像素间距；零表示不执行区域合并。</summary>
    public int MergeDistance { get; set; }

    /// <summary>获取或设置外接框在每个方向按自身宽高扩展的比例。</summary>
    public double PaddingRatio { get; set; }
}

/// <summary>
/// 对 Anomalib 二值异常掩码执行高效连通域与区域后处理。
/// </summary>
public static class AnomalibRegionProcessor
{
    /// <summary>
    /// 使用八邻域提取连通域，并依次执行最小面积过滤、邻近合并和扩框。
    /// </summary>
    /// <param name="mask">按行连续存储的二值掩码，非零值均视为异常。</param>
    /// <param name="width">掩码宽度。</param>
    /// <param name="height">掩码高度。</param>
    /// <param name="options">区域后处理选项。</param>
    /// <returns>按纵坐标、横坐标稳定排序的异常区域。</returns>
    public static IReadOnlyList<AnomalibMaskRegion> Extract(
        ReadOnlySpan<byte> mask,
        int width,
        int height,
        AnomalibRegionOptions? options = null)
    {
        ValidateArguments(mask, width, height, options ??= new AnomalibRegionOptions());
        var pixelCount = checked(width * height);
        var visited = new bool[pixelCount];
        var queue = ArrayPool<int>.Shared.Rent(pixelCount);
        var regions = new List<MutableRegion>();
        try
        {
            for (var index = 0; index < pixelCount; index++)
            {
                if (mask[index] == 0 || visited[index])
                {
                    continue;
                }

                var region = FloodFill(mask, width, height, index, visited, queue);
                if (region.PixelArea >= options.MinimumArea)
                {
                    regions.Add(region);
                }
            }
        }
        finally
        {
            ArrayPool<int>.Shared.Return(queue);
        }

        MergeNearby(regions, options.MergeDistance);
        var result = regions
            .Select(region => new AnomalibMaskRegion
            {
                Bounds = ExpandAndClamp(region.ToRectangle(), options.PaddingRatio, width, height),
                PixelArea = region.PixelArea
            })
            .OrderBy(static region => region.Bounds.Y)
            .ThenBy(static region => region.Bounds.X)
            .ToArray();
        return result;
    }

    /// <summary>
    /// 校验掩码尺寸与后处理选项。
    /// </summary>
    /// <param name="mask">二值掩码。</param>
    /// <param name="width">掩码宽度。</param>
    /// <param name="height">掩码高度。</param>
    /// <param name="options">区域选项。</param>
    private static void ValidateArguments(ReadOnlySpan<byte> mask, int width, int height, AnomalibRegionOptions options)
    {
        if (width <= 0 || height <= 0 || mask.Length != checked(width * height))
        {
            throw new ArgumentException("异常掩码长度必须等于宽度乘以高度。", nameof(mask));
        }
        if (options.MinimumArea <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(options), "最小区域面积必须大于零。");
        }
        if (options.MergeDistance < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(options), "区域合并距离不能为负数。");
        }
        if (!double.IsFinite(options.PaddingRatio) || options.PaddingRatio < 0 || options.PaddingRatio > 10)
        {
            throw new ArgumentOutOfRangeException(nameof(options), "扩框比例必须是 0 到 10 之间的有限数值。");
        }
    }

    /// <summary>
    /// 从指定异常像素开始执行八邻域广度优先搜索。
    /// </summary>
    /// <param name="mask">二值掩码。</param>
    /// <param name="width">掩码宽度。</param>
    /// <param name="height">掩码高度。</param>
    /// <param name="startIndex">起始像素索引。</param>
    /// <param name="visited">像素访问标记。</param>
    /// <param name="queue">复用的广度优先搜索队列。</param>
    /// <returns>连通域的边界和面积。</returns>
    private static MutableRegion FloodFill(
        ReadOnlySpan<byte> mask,
        int width,
        int height,
        int startIndex,
        bool[] visited,
        int[] queue)
    {
        var head = 0;
        var tail = 0;
        queue[tail++] = startIndex;
        visited[startIndex] = true;
        var startX = startIndex % width;
        var startY = startIndex / width;
        var region = new MutableRegion(startX, startY);

        while (head < tail)
        {
            var index = queue[head++];
            var x = index % width;
            var y = index / width;
            region.Include(x, y);

            var minY = Math.Max(0, y - 1);
            var maxY = Math.Min(height - 1, y + 1);
            var minX = Math.Max(0, x - 1);
            var maxX = Math.Min(width - 1, x + 1);
            for (var neighborY = minY; neighborY <= maxY; neighborY++)
            {
                var rowOffset = neighborY * width;
                for (var neighborX = minX; neighborX <= maxX; neighborX++)
                {
                    var neighborIndex = rowOffset + neighborX;
                    if (!visited[neighborIndex] && mask[neighborIndex] != 0)
                    {
                        visited[neighborIndex] = true;
                        queue[tail++] = neighborIndex;
                    }
                }
            }
        }

        return region;
    }

    /// <summary>
    /// 反复合并距离满足条件的区域，确保传递相邻关系也被合并。
    /// </summary>
    /// <param name="regions">可变区域集合。</param>
    /// <param name="maximumDistance">允许的最大横纵间距。</param>
    private static void MergeNearby(List<MutableRegion> regions, int maximumDistance)
    {
        if (maximumDistance == 0 || regions.Count < 2) { return; }
        var changed = true;
        while (changed)
        {
            changed = false;
            for (var leftIndex = 0; leftIndex < regions.Count && !changed; leftIndex++)
            {
                for (var rightIndex = leftIndex + 1; rightIndex < regions.Count; rightIndex++)
                {
                    if (!AreNear(regions[leftIndex], regions[rightIndex], maximumDistance))
                    {
                        continue;
                    }

                    regions[leftIndex].Merge(regions[rightIndex]);
                    regions.RemoveAt(rightIndex);
                    changed = true;
                    break;
                }
            }
        }
    }

    /// <summary>
    /// 判断两个区域在横向和纵向上的间隔是否均不超过阈值。
    /// </summary>
    /// <param name="left">第一个区域。</param>
    /// <param name="right">第二个区域。</param>
    /// <param name="maximumDistance">最大允许间隔。</param>
    /// <returns>区域相交或足够接近时返回 true。</returns>
    private static bool AreNear(MutableRegion left, MutableRegion right, int maximumDistance)
    {
        var horizontalGap = Math.Max(0, Math.Max(left.MinX - right.MaxX - 1, right.MinX - left.MaxX - 1));
        var verticalGap = Math.Max(0, Math.Max(left.MinY - right.MaxY - 1, right.MinY - left.MaxY - 1));
        return horizontalGap <= maximumDistance && verticalGap <= maximumDistance;
    }

    /// <summary>
    /// 按比例扩展矩形并裁剪到掩码边界。
    /// </summary>
    /// <param name="rectangle">原始矩形。</param>
    /// <param name="paddingRatio">每侧扩展比例。</param>
    /// <param name="width">掩码宽度。</param>
    /// <param name="height">掩码高度。</param>
    /// <returns>位于掩码范围内的扩展矩形。</returns>
    private static PixelRectangle ExpandAndClamp(PixelRectangle rectangle, double paddingRatio, int width, int height)
    {
        var paddingX = (int)Math.Ceiling(rectangle.Width * paddingRatio);
        var paddingY = (int)Math.Ceiling(rectangle.Height * paddingRatio);
        var left = Math.Max(0, rectangle.X - paddingX);
        var top = Math.Max(0, rectangle.Y - paddingY);
        var right = Math.Min(width, rectangle.Right + paddingX);
        var bottom = Math.Min(height, rectangle.Bottom + paddingY);
        return new PixelRectangle(left, top, right - left, bottom - top);
    }

    /// <summary>
    /// 连通域扫描期间使用的可变区域。
    /// </summary>
    private sealed class MutableRegion
    {
        /// <summary>
        /// 使用首个像素创建区域。
        /// </summary>
        /// <param name="x">像素横坐标。</param>
        /// <param name="y">像素纵坐标。</param>
        public MutableRegion(int x, int y)
        {
            MinX = MaxX = x;
            MinY = MaxY = y;
        }

        /// <summary>获取区域最小横坐标。</summary>
        public int MinX { get; private set; }

        /// <summary>获取区域最小纵坐标。</summary>
        public int MinY { get; private set; }

        /// <summary>获取区域最大横坐标。</summary>
        public int MaxX { get; private set; }

        /// <summary>获取区域最大纵坐标。</summary>
        public int MaxY { get; private set; }

        /// <summary>获取区域异常像素数量。</summary>
        public int PixelArea { get; private set; }

        /// <summary>
        /// 把一个异常像素纳入区域。
        /// </summary>
        /// <param name="x">像素横坐标。</param>
        /// <param name="y">像素纵坐标。</param>
        public void Include(int x, int y)
        {
            MinX = Math.Min(MinX, x);
            MinY = Math.Min(MinY, y);
            MaxX = Math.Max(MaxX, x);
            MaxY = Math.Max(MaxY, y);
            PixelArea++;
        }

        /// <summary>
        /// 把另一个区域合并到当前区域。
        /// </summary>
        /// <param name="other">待合并区域。</param>
        public void Merge(MutableRegion other)
        {
            MinX = Math.Min(MinX, other.MinX);
            MinY = Math.Min(MinY, other.MinY);
            MaxX = Math.Max(MaxX, other.MaxX);
            MaxY = Math.Max(MaxY, other.MaxY);
            PixelArea = checked(PixelArea + other.PixelArea);
        }

        /// <summary>
        /// 把包含式边界转换为左上角加宽高的矩形。
        /// </summary>
        /// <returns>区域外接矩形。</returns>
        public PixelRectangle ToRectangle() => new(MinX, MinY, MaxX - MinX + 1, MaxY - MinY + 1);
    }
}

/// <summary>
/// 把模型异常图坐标映射回原始图片像素坐标。
/// </summary>
public static class AnomalibCoordinateMapper
{
    /// <summary>
    /// 按模型预处理方式执行异常区域逆变换，并裁剪到原图范围。
    /// </summary>
    /// <param name="rectangle">异常图坐标中的矩形。</param>
    /// <param name="mapWidth">异常图宽度。</param>
    /// <param name="mapHeight">异常图高度。</param>
    /// <param name="inputWidth">模型输入宽度。</param>
    /// <param name="inputHeight">模型输入高度。</param>
    /// <param name="originalWidth">原图宽度。</param>
    /// <param name="originalHeight">原图高度。</param>
    /// <param name="resizeMode">模型预处理缩放方式。</param>
    /// <returns>覆盖原异常区域且不超出原图的像素矩形。</returns>
    public static PixelRectangle MapToOriginal(
        PixelRectangle rectangle,
        int mapWidth,
        int mapHeight,
        int inputWidth,
        int inputHeight,
        int originalWidth,
        int originalHeight,
        AnomalibResizeMode resizeMode)
    {
        ValidateDimensions(rectangle, mapWidth, mapHeight, inputWidth, inputHeight, originalWidth, originalHeight);
        var inputLeft = rectangle.X * (double)inputWidth / mapWidth;
        var inputTop = rectangle.Y * (double)inputHeight / mapHeight;
        var inputRight = rectangle.Right * (double)inputWidth / mapWidth;
        var inputBottom = rectangle.Bottom * (double)inputHeight / mapHeight;

        double originalLeft;
        double originalTop;
        double originalRight;
        double originalBottom;
        switch (resizeMode)
        {
            case AnomalibResizeMode.Stretch:
                originalLeft = inputLeft * originalWidth / inputWidth;
                originalTop = inputTop * originalHeight / inputHeight;
                originalRight = inputRight * originalWidth / inputWidth;
                originalBottom = inputBottom * originalHeight / inputHeight;
                break;
            case AnomalibResizeMode.Letterbox:
                var fitScale = Math.Min(inputWidth / (double)originalWidth, inputHeight / (double)originalHeight);
                var paddingX = (inputWidth - originalWidth * fitScale) / 2d;
                var paddingY = (inputHeight - originalHeight * fitScale) / 2d;
                originalLeft = (inputLeft - paddingX) / fitScale;
                originalTop = (inputTop - paddingY) / fitScale;
                originalRight = (inputRight - paddingX) / fitScale;
                originalBottom = (inputBottom - paddingY) / fitScale;
                break;
            case AnomalibResizeMode.CenterCrop:
                var fillScale = Math.Max(inputWidth / (double)originalWidth, inputHeight / (double)originalHeight);
                var cropX = (originalWidth * fillScale - inputWidth) / 2d;
                var cropY = (originalHeight * fillScale - inputHeight) / 2d;
                originalLeft = (inputLeft + cropX) / fillScale;
                originalTop = (inputTop + cropY) / fillScale;
                originalRight = (inputRight + cropX) / fillScale;
                originalBottom = (inputBottom + cropY) / fillScale;
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(resizeMode), resizeMode, "未知的 Anomalib 缩放方式。");
        }

        return ToClampedRectangle(originalLeft, originalTop, originalRight, originalBottom, originalWidth, originalHeight);
    }

    /// <summary>
    /// 校验所有尺寸为正数且输入矩形位于异常图范围内。
    /// </summary>
    /// <param name="rectangle">异常图矩形。</param>
    /// <param name="mapWidth">异常图宽度。</param>
    /// <param name="mapHeight">异常图高度。</param>
    /// <param name="inputWidth">输入宽度。</param>
    /// <param name="inputHeight">输入高度。</param>
    /// <param name="originalWidth">原图宽度。</param>
    /// <param name="originalHeight">原图高度。</param>
    private static void ValidateDimensions(
        PixelRectangle rectangle,
        int mapWidth,
        int mapHeight,
        int inputWidth,
        int inputHeight,
        int originalWidth,
        int originalHeight)
    {
        if (mapWidth <= 0 || mapHeight <= 0 || inputWidth <= 0 || inputHeight <= 0 || originalWidth <= 0 || originalHeight <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(mapWidth), "异常图、模型输入和原图尺寸都必须大于零。");
        }
        if (rectangle.Right > mapWidth || rectangle.Bottom > mapHeight)
        {
            throw new ArgumentOutOfRangeException(nameof(rectangle), "异常区域不能超出异常图范围。");
        }
    }

    /// <summary>
    /// 使用向外取整把浮点边界转换为原图内的像素矩形。
    /// </summary>
    /// <param name="left">浮点左边界。</param>
    /// <param name="top">浮点上边界。</param>
    /// <param name="right">浮点右边界。</param>
    /// <param name="bottom">浮点下边界。</param>
    /// <param name="width">原图宽度。</param>
    /// <param name="height">原图高度。</param>
    /// <returns>裁剪后的整数矩形。</returns>
    private static PixelRectangle ToClampedRectangle(
        double left,
        double top,
        double right,
        double bottom,
        int width,
        int height)
    {
        if (right <= 0 || bottom <= 0 || left >= width || top >= height)
        {
            throw new InvalidOperationException("异常区域完全位于预处理填充区，无法映射到原图。");
        }
        var x1 = Math.Clamp((int)Math.Floor(left), 0, width - 1);
        var y1 = Math.Clamp((int)Math.Floor(top), 0, height - 1);
        var x2 = Math.Clamp((int)Math.Ceiling(right), x1 + 1, width);
        var y2 = Math.Clamp((int)Math.Ceiling(bottom), y1 + 1, height);
        return new PixelRectangle(x1, y1, x2 - x1, y2 - y1);
    }
}
