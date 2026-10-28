using SkiaSharp;

namespace Snet.Yolo.Server.handler;

/// <summary>按浏览器一致的 EXIF 方向解码，统一标注和推理的像素坐标空间。</summary>
public static class ImageDecoder
{
    /// <summary>解码本地图片并摆正方向。</summary>
    public static SKBitmap Decode(string path)
    {
        using var codec = SKCodec.Create(path) ?? throw new InvalidDataException("无法解码图片。");
        return Decode(codec);
    }

    /// <summary>解码图片字节并摆正方向。</summary>
    public static SKBitmap Decode(byte[] bytes)
    {
        using var data = SKData.CreateCopy(bytes);
        using var codec = SKCodec.Create(data) ?? throw new InvalidDataException("无法解码图片。");
        return Decode(codec);
    }

    /// <summary>由编码器读取 EXIF 旋转或镜像，返回调用方负责释放的位图。</summary>
    public static SKBitmap Decode(SKCodec codec)
        => Decode(codec, 0);

    /// <summary>限制解码后最长边；JPEG 优先使用原生缩小解码，再摆正 EXIF 方向。0 表示保留原尺寸。</summary>
    public static SKBitmap Decode(SKCodec codec, int maximumEdge)
    {
        var info = codec.Info;
        var longest = Math.Max(info.Width, info.Height);
        if (maximumEdge > 0 && longest > maximumEdge)
        {
            var dimensions = codec.GetScaledDimensions(maximumEdge / (float)longest);
            info = info.WithSize(dimensions.Width, dimensions.Height);
        }
        var decoded = SKBitmap.Decode(codec, info) ?? throw new InvalidDataException("无法解码图片。");
        // PNG/BMP 等格式可能不支持原生缩小解码；先缩小再旋转，避免同时保留两张原尺寸位图。
        longest = Math.Max(decoded.Width, decoded.Height);
        if (maximumEdge > 0 && longest > maximumEdge)
        {
            using var fullSize = decoded;
            var scale = maximumEdge / (double)longest;
            decoded = fullSize.Resize(new SKImageInfo(Math.Max(1, (int)Math.Round(fullSize.Width * scale)), Math.Max(1, (int)Math.Round(fullSize.Height * scale))),
                new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.None)) ?? throw new InvalidDataException("无法缩小图片。");
        }
        if (codec.EncodedOrigin == SKEncodedOrigin.TopLeft) { return decoded; }
        using (decoded)
        {
            var swaps = codec.EncodedOrigin is SKEncodedOrigin.LeftTop or SKEncodedOrigin.RightTop or SKEncodedOrigin.RightBottom or SKEncodedOrigin.LeftBottom;
            var width = swaps ? decoded.Height : decoded.Width;
            var height = swaps ? decoded.Width : decoded.Height;
            var result = new SKBitmap(width, height);
            try
            {
                using var canvas = new SKCanvas(result);
                switch (codec.EncodedOrigin)
                {
                    case SKEncodedOrigin.TopRight: canvas.Translate(width, 0); canvas.Scale(-1, 1); break;
                    case SKEncodedOrigin.BottomRight: canvas.Translate(width, height); canvas.RotateDegrees(180); break;
                    case SKEncodedOrigin.BottomLeft: canvas.Translate(0, height); canvas.Scale(1, -1); break;
                    case SKEncodedOrigin.LeftTop: canvas.RotateDegrees(90); canvas.Scale(1, -1); break;
                    case SKEncodedOrigin.RightTop: canvas.Translate(width, 0); canvas.RotateDegrees(90); break;
                    case SKEncodedOrigin.RightBottom: canvas.Translate(width, height); canvas.RotateDegrees(90); canvas.Scale(-1, 1); break;
                    case SKEncodedOrigin.LeftBottom: canvas.Translate(0, height); canvas.RotateDegrees(-90); break;
                }
                canvas.DrawBitmap(decoded, 0, 0, new SKSamplingOptions(SKFilterMode.Nearest));
                return result;
            }
            catch { result.Dispose(); throw; }
        }
    }
}
