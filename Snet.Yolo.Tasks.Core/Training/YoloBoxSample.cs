namespace Snet.Yolo.Tasks.Core.Training;

/// <summary>一个标注框在原图上的尺寸：框的短边（原图像素）与所在图片的长边（原图像素）。</summary>
/// <param name="ShortSidePixels">框短边，原图像素。</param>
/// <param name="LongEdgePixels">图片长边，原图像素。</param>
public readonly record struct YoloBoxSample(double ShortSidePixels, int LongEdgePixels)
{
    /// <summary>训练输入边长为 <paramref name="imageSize"/> 时，这个框在网络输入上的短边像素（图片按长边等比缩放到该尺寸）。</summary>
    /// <param name="imageSize">训练输入边长（imgsz）。</param>
    public double PixelsAt(int imageSize) => LongEdgePixels <= 0 ? 0 : ShortSidePixels * imageSize / LongEdgePixels;
}
