namespace Snet.Yolo.Server.anomalib;

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
