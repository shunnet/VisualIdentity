using System.Buffers;

namespace Snet.Yolo.Server.anomalib;

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
