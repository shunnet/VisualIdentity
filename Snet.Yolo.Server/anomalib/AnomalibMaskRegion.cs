namespace Snet.Yolo.Server.anomalib;

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
