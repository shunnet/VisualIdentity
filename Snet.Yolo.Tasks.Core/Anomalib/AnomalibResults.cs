namespace Snet.Yolo.Tasks.Core.Anomalib;

/// <summary>
/// 整数像素矩形，采用左上角坐标和宽高表示。
/// </summary>
public readonly record struct PixelRectangle
{
    /// <summary>
    /// 创建像素矩形。
    /// </summary>
    /// <param name="x">左上角横坐标。</param>
    /// <param name="y">左上角纵坐标。</param>
    /// <param name="width">矩形宽度。</param>
    /// <param name="height">矩形高度。</param>
    public PixelRectangle(int x, int y, int width, int height)
    {
        if (x < 0 || y < 0 || width <= 0 || height <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(width), "像素矩形坐标不能为负数，宽高必须大于零。");
        }
        X = x;
        Y = y;
        Width = width;
        Height = height;
    }

    /// <summary>获取左上角横坐标。</summary>
    public int X { get; }

    /// <summary>获取左上角纵坐标。</summary>
    public int Y { get; }

    /// <summary>获取矩形宽度。</summary>
    public int Width { get; }

    /// <summary>获取矩形高度。</summary>
    public int Height { get; }

    /// <summary>获取不包含在矩形内的右边界坐标。</summary>
    public int Right => checked(X + Width);

    /// <summary>获取不包含在矩形内的下边界坐标。</summary>
    public int Bottom => checked(Y + Height);
}

/// <summary>
/// 从二值异常掩码中提取的区域。
/// </summary>
public sealed class AnomalibMaskRegion
{
    /// <summary>获取或初始化区域外接矩形。</summary>
    public required PixelRectangle Bounds { get; init; }

    /// <summary>获取或初始化区域内的异常像素数量。</summary>
    public required int PixelArea { get; init; }
}

/// <summary>
/// Anomalib 单个异常区域结果。
/// </summary>
public sealed class AnomalibRegionResult
{
    /// <summary>获取或初始化从 1 开始的区域编号。</summary>
    public required int RegionId { get; init; }

    /// <summary>获取或初始化原图像素坐标中的外接矩形。</summary>
    public required PixelRectangle Bounds { get; init; }

    /// <summary>获取或初始化模型异常掩码中的像素面积。</summary>
    public required int PixelArea { get; init; }

    /// <summary>获取或初始化区域内的最大异常分数。</summary>
    public required float MaximumScore { get; init; }

    /// <summary>获取或初始化区域内的平均异常分数。</summary>
    public required float MeanScore { get; init; }
}

/// <summary>
/// Anomalib 单张图片推理结果。
/// </summary>
public sealed class AnomalibImageResult
{
    /// <summary>获取或初始化整图异常分数。</summary>
    public required float ImageScore { get; init; }

    /// <summary>获取或初始化整图是否被判定为异常。</summary>
    public required bool IsAnomalous { get; init; }

    /// <summary>获取或初始化原图宽度。</summary>
    public required int OriginalWidth { get; init; }

    /// <summary>获取或初始化原图高度。</summary>
    public required int OriginalHeight { get; init; }

    /// <summary>获取或初始化异常图宽度。</summary>
    public required int MapWidth { get; init; }

    /// <summary>获取或初始化异常图高度。</summary>
    public required int MapHeight { get; init; }

    /// <summary>获取或初始化纯模型推理耗时，单位为毫秒。</summary>
    public required long InferenceMilliseconds { get; init; }

    /// <summary>获取或初始化映射到原图坐标的异常区域。</summary>
    public required IReadOnlyList<AnomalibRegionResult> Regions { get; init; }
}
