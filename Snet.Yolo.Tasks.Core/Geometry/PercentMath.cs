namespace Snet.Yolo.Tasks.Core.Geometry;

/// <summary>
/// Label Studio 图像坐标单位换算：序列化值一律为原始图像尺寸的 0–100 百分数；
/// 像素换算公式为 pixel = percent / 100 * original。
/// </summary>
public static class PercentMath
{
    /// <summary>将百分数坐标转换为原始图像像素坐标。</summary>
    public static double PercentToPixels(double percent, double originalDimension) => percent / 100d * originalDimension;

    /// <summary>将原始图像像素坐标转换为百分数坐标。</summary>
    public static double PixelsToPercent(double pixels, double originalDimension) => pixels / originalDimension * 100d;
}
