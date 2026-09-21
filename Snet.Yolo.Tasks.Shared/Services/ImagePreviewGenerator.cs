namespace Snet.Yolo.Tasks.Services;

using SkiaSharp;

/// <summary>验证页图片优化选项（配置节 <c>Images:Preview</c>）。</summary>
public sealed class ImagePreviewOptions
{
    /// <summary>是否启用（默认启用）。</summary>
    public bool Enabled { get; set; } = true;
    /// <summary>预览图允许的最大字节数（默认 400 KiB）。</summary>
    public long TargetBytes { get; set; } = 400L * 1024;
    /// <summary>预览图最长边上限（默认 1600）。超过则等比缩小；设为 0 表示不限制尺寸。</summary>
    public int MaxEdge { get; set; } = 1600;
    /// <summary>起始 JPEG 质量。</summary>
    public int StartQuality { get; set; } = 82;
    /// <summary>最低 JPEG 质量（再低就靠缩尺寸达标）。</summary>
    public int MinQuality { get; set; } = 60;
}

/// <summary>优化结果。</summary>
/// <param name="Data">优化后的字节（JPEG）。</param>
/// <param name="Width">宽。</param>
/// <param name="Height">高。</param>
/// <param name="Quality">最终 JPEG 质量。</param>
/// <param name="OriginalBytes">原始字节数。</param>
/// <param name="Note">面向用户的说明（中文，可空）。</param>
public sealed record GeneratedImagePreview(byte[] Data, int Width, int Height, int Quality, long OriginalBytes, string? Note)
{
    /// <summary>优化后的字节数。</summary>
    public long Bytes => Data.LongLength;
}

/// <summary>
/// 验证页预览图生成：把现场大图（例如 5120×5120、75 MB 的 BMP）**按需**生成一张
/// 「最长边 ≤ MaxEdge、体积 ≤ 400 KiB」的 JPEG 预览。**原图保持不变**（上传不做任何加工），
/// 页面列表与主视图只加载预览，浏览器因此不必解码 5120×5120 的大位图。</summary>
public static class ImagePreviewGenerator
{
    private static readonly HashSet<string> LosslessExtensions = new(StringComparer.OrdinalIgnoreCase) { ".jpg", ".jpeg" };

    /// <summary>是否需要优化：非 JPEG（BMP/PNG 等）或体积超预算或尺寸超限。</summary>
    /// <param name="fileName">原始文件名（用于判断格式）。</param>
    /// <param name="bytes">原始字节数。</param>
    /// <param name="width">原始宽（未知传 0）。</param>
    /// <param name="height">原始高（未知传 0）。</param>
    /// <param name="options">选项。</param>
    public static bool ShouldGenerate(string fileName, long bytes, int width, int height, ImagePreviewOptions options)
    {
        if (!options.Enabled) { return false; }
        if (!LosslessExtensions.Contains(Path.GetExtension(fileName))) { return true; }
        if (bytes > options.TargetBytes) { return true; }
        return options.MaxEdge > 0 && Math.Max(width, height) > options.MaxEdge;
    }

    /// <summary>
    /// 读取图片、按需缩放并编码为 JPEG，直到满足「尺寸与体积」双约束。
    /// 解码失败（不是有效图片）时抛 <see cref="InvalidDataException"/>，由调用方按上传失败处理。
    /// </summary>
    /// <param name="source">原始文件路径。</param>
    /// <param name="options">选项。</param>
    public static GeneratedImagePreview Generate(string source, ImagePreviewOptions options)
    {
        var original = new FileInfo(source).Length;
        using var codec = SKCodec.Create(source) ?? throw new InvalidDataException("无法解码图片：" + Path.GetFileName(source));
        var origin = codec.EncodedOrigin;
        // JPEG 支持按比例解码：直接解到目标尺寸附近，比先解 5120² 再缩快数倍（BMP 不支持，只能全解）
        var decodeInfo = codec.Info;
        if (options.MaxEdge > 0 && Math.Max(decodeInfo.Width, decodeInfo.Height) > options.MaxEdge && IsScalableCodec(source))
        {
            var scaled = codec.GetScaledDimensions(Math.Min(1f, options.MaxEdge / (float)Math.Max(decodeInfo.Width, decodeInfo.Height)));
            decodeInfo = decodeInfo.WithSize(scaled);
        }
        using var decoded = SKBitmap.Decode(codec, decodeInfo) ?? throw new InvalidDataException("无法解码图片：" + Path.GetFileName(source));

        // 先按最长边限制算目标尺寸（EXIF 旋转 90/270 时宽高互换）
        var swapped = origin is SKEncodedOrigin.LeftTop or SKEncodedOrigin.RightTop or SKEncodedOrigin.RightBottom or SKEncodedOrigin.LeftBottom;
        var sourceWidth = swapped ? decoded.Height : decoded.Width;
        var sourceHeight = swapped ? decoded.Width : decoded.Height;
        var scale = options.MaxEdge > 0 && Math.Max(sourceWidth, sourceHeight) > options.MaxEdge
            ? options.MaxEdge / (double)Math.Max(sourceWidth, sourceHeight)
            : 1d;

        var quality = Math.Clamp(options.StartQuality, 40, 100);
        var minQuality = Math.Clamp(options.MinQuality, 30, quality);
        byte[]? best = null;
        var bestQuality = quality;
        var bestWidth = 0;
        var bestHeight = 0;
        for (var attempt = 0; attempt < 8; attempt++)
        {
            var width = Math.Max(1, (int)Math.Round(sourceWidth * scale));
            var height = Math.Max(1, (int)Math.Round(sourceHeight * scale));
            var data = Encode(decoded, origin, width, height, quality);
            if (best is null || data.LongLength < best.LongLength)
            {
                best = data;
                bestQuality = quality;
                bestWidth = width;
                bestHeight = height;
            }
            if (data.LongLength <= options.TargetBytes && quality <= options.StartQuality) { return Result(data, width, height, quality, original); }
            // 还能降质量就继续降：不允许缩尺寸（MaxEdge=0）时用硬下限 40，尽量满足体积预算
            var floor = options.MaxEdge > 0 ? minQuality : Math.Min(minQuality, 40);
            if (quality > floor) { quality = Math.Max(floor, quality - 5); continue; }
            if (options.MaxEdge > 0 && width > options.MaxEdge / 2) { scale *= 0.85; continue; }   // 质量到底了还不达标 → 继续缩尺寸
            break;
        }

        var fallback = best ?? throw new InvalidDataException("图片压缩失败：" + Path.GetFileName(source));
        var note = fallback.LongLength > options.TargetBytes
            ? $"已尽量压缩到 {Format(fallback.LongLength)}（超过 {Format(options.TargetBytes)} 目标）"
            : null;
        return Result(fallback, bestWidth, bestHeight, bestQuality, original) with { Note = note };
    }

    private static GeneratedImagePreview Result(byte[] data, int width, int height, int quality, long original)
        => new(data, width, height, quality, original, $"已优化：{Format(original)} → {Format(data.LongLength)}（{width}×{height}，JPEG q{quality}）");

    /// <summary>该格式是否支持按比例解码（JPEG 支持；BMP/PNG 只能全尺寸解码）。</summary>
    private static bool IsScalableCodec(string path)
    {
        var extension = Path.GetExtension(path);
        return extension.Equals(".jpg", StringComparison.OrdinalIgnoreCase) || extension.Equals(".jpeg", StringComparison.OrdinalIgnoreCase);
    }
    /// <summary>按 EXIF 方向绘制到目标尺寸并编码 JPEG（照片方向正确，且不留透明通道）。</summary>
    private static byte[] Encode(SKBitmap source, SKEncodedOrigin origin, int width, int height, int quality)
    {
        var info = new SKImageInfo(width, height, SKColorType.Rgb888x, SKAlphaType.Opaque);
        using var surface = SKSurface.Create(info);
        surface.Canvas.Clear(SKColors.White);
        using var image = SKImage.FromBitmap(source);
        surface.Canvas.Save();
        ApplyOrigin(surface.Canvas, origin, width, height);
        var sampling = new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear);
        var swapsAxes = origin is SKEncodedOrigin.LeftTop or SKEncodedOrigin.RightTop or SKEncodedOrigin.RightBottom or SKEncodedOrigin.LeftBottom;
        var drawWidth = swapsAxes ? height : width;
        var drawHeight = swapsAxes ? width : height;
        surface.Canvas.DrawImage(image, new SKRect(0, 0, drawWidth, drawHeight), sampling);
        surface.Canvas.Restore();
        using var snapshot = surface.Snapshot();
        using var encoded = snapshot.Encode(SKEncodedImageFormat.Jpeg, quality) ?? throw new InvalidDataException("JPEG 编码失败。");
        return encoded.ToArray();
    }

    /// <summary>把画布按 EXIF 方向摆正（缩放由 DrawImage 承担，这里只处理旋转/镜像）。</summary>
    private static void ApplyOrigin(SKCanvas canvas, SKEncodedOrigin origin, int width, int height)
    {
        switch (origin)
        {
            case SKEncodedOrigin.TopRight:
                canvas.Translate(width, 0);
                canvas.Scale(-1, 1);
                break;
            case SKEncodedOrigin.BottomRight:
                canvas.Translate(width, height);
                canvas.RotateDegrees(180);
                break;
            case SKEncodedOrigin.BottomLeft:
                canvas.Translate(0, height);
                canvas.Scale(1, -1);
                break;
            case SKEncodedOrigin.LeftTop:
                canvas.RotateDegrees(90);
                canvas.Scale(1, -1);
                break;
            case SKEncodedOrigin.RightTop:
                canvas.Translate(width, 0);
                canvas.RotateDegrees(90);
                break;
            case SKEncodedOrigin.RightBottom:
                canvas.Translate(width, height);
                canvas.RotateDegrees(-90);
                break;
            case SKEncodedOrigin.LeftBottom:
                canvas.Translate(0, height);
                canvas.RotateDegrees(-90);
                canvas.Scale(1, -1);
                break;
        }
    }

    /// <summary>把字节数格式化为便于阅读的文本。</summary>
    public static string Format(long bytes) => bytes >= 1024L * 1024 * 1024
        ? (bytes / (1024d * 1024 * 1024)).ToString("0.##") + " GB"
        : bytes >= 1024 * 1024 ? (bytes / (1024d * 1024)).ToString("0.#") + " MB" : (bytes / 1024d).ToString("0.#") + " KB";
}
