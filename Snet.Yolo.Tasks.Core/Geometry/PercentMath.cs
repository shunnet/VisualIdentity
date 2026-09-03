namespace Snet.Yolo.Tasks.Core.Geometry;

/// <summary>
/// Label Studio 图像坐标单位换算：序列化值一律为原始图像尺寸的 0–100 百分数；
/// 像素换算公式见官方 image_units 文档：pixel = percent / 100 * original。
/// </summary>
public static class PercentMath
{
    /// <summary>百分数转像素。</summary>
    public static double PercentToPixels(double percent, double originalDimension) => percent / 100d * originalDimension;

    /// <summary>像素转百分数。</summary>
    public static double PixelsToPercent(double pixels, double originalDimension) => pixels / originalDimension * 100d;
}
