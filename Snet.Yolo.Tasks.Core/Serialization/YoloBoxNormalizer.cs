using System.Globalization;

namespace Snet.Yolo.Tasks.Core.Serialization;

/// <summary>导入与导出共用的 YOLO 矩形边界规则，只修正旧版文本舍入造成的微小越界。</summary>
public static class YoloBoxNormalizer
{
    /// <summary>旧版五位小数的中心与宽高独立舍入最多产生 0.0000075 的边界误差，保留少量浮点余量。</summary>
    public const double BoundaryTolerance = 0.00001;

    /// <summary>验证有限归一化坐标，微小越界贴齐图边缘，真实越界保留明确失败。</summary>
    /// <param name="xCenter">归一化中心 X。</param>
    /// <param name="yCenter">归一化中心 Y。</param>
    /// <param name="width">归一化正宽度。</param>
    /// <param name="height">归一化正高度。</param>
    /// <returns>可导出和导入的中心、宽高，以及是否发生边界调整。</returns>
    public static (double XCenter, double YCenter, double Width, double Height, bool BoundaryAdjusted) Normalize(double xCenter, double yCenter, double width, double height)
    {
        if (!double.IsFinite(xCenter) || !double.IsFinite(yCenter) || !double.IsFinite(width) || !double.IsFinite(height))
        { throw new InvalidDataException("包含无效坐标。"); }
        if (xCenter is < 0 or > 1 || yCenter is < 0 or > 1 || width is <= 0 or > 1 || height is <= 0 or > 1)
        { throw new InvalidDataException("坐标必须位于 0 到 1，宽高必须大于 0。"); }

        var left = xCenter - width / 2; var right = xCenter + width / 2;
        var top = yCenter - height / 2; var bottom = yCenter + height / 2;
        if (left < -BoundaryTolerance || right > 1 + BoundaryTolerance || top < -BoundaryTolerance || bottom > 1 + BoundaryTolerance)
        {
            throw new InvalidDataException(string.Create(CultureInfo.InvariantCulture,
                $"标注框超出图片范围（左={left:G9}，上={top:G9}，右={right:G9}，下={bottom:G9}；边界容差={BoundaryTolerance:G}）。"));
        }
        var adjusted = false;
        if (left < 0 || right > 1)
        {
            left = Math.Max(0, left); right = Math.Min(1, right);
            xCenter = (left + right) / 2; width = right - left; adjusted = true;
        }
        if (top < 0 || bottom > 1)
        {
            top = Math.Max(0, top); bottom = Math.Min(1, bottom);
            yCenter = (top + bottom) / 2; height = bottom - top; adjusted = true;
        }
        return (xCenter, yCenter, width, height, adjusted);
    }
}
