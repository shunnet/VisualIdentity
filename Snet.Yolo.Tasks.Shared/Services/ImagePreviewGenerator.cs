namespace Snet.Yolo.Tasks.Services;

using SkiaSharp;

/// <summary>
/// 验证页/列表页的派生图生成：把现场大图（例如 5120×5120、75 MB 的 BMP）**按需**生成
/// 「预览图」（最长边 ≤ MaxEdge、体积 ≤ 400 KiB，用于主视图）与「缩略图」（最长边 ≤ ThumbMaxEdge、通常十几 KB，用于列表/网格）。
/// **原图保持不变**（上传不做任何加工），页面只加载派生图，浏览器因此不必解码 5120×5120 的大位图。
/// </summary>
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

    /// <summary>原图本身是否已经小到可以直接当缩略图用（小体积 JPEG）。</summary>
    /// <param name="fileName">原始文件名（用于判断格式）。</param>
    /// <param name="bytes">原始字节数。</param>
    /// <param name="options">选项。</param>
    public static bool IsThumbnailSized(string fileName, long bytes, ImagePreviewOptions options)
        => LosslessExtensions.Contains(Path.GetExtension(fileName)) && bytes <= options.ThumbTargetBytes;

    /// <summary>
    /// 读取图片、按需缩放并编码为预览 JPEG，直到满足「尺寸与体积」双约束。
    /// 解码失败（不是有效图片）时抛 <see cref="InvalidDataException"/>，由调用方按上传失败处理。
    /// </summary>
    /// <param name="source">原始文件路径。</param>
    /// <param name="options">选项。</param>
    public static GeneratedImagePreview Generate(string source, ImagePreviewOptions options)
    {
        using var decoded = Decode(source, options.MaxEdge);
        return CompressPreview(decoded, options);
    }

    /// <summary>
    /// 生成列表/网格用的缩略图。源文件可以是原图，也可以是已生成的预览图（后者只需解码一张 ≤1600 像素的小图，快几十倍）。
    /// </summary>
    /// <param name="source">原始或预览文件路径。</param>
    /// <param name="options">选项。</param>
    public static GeneratedImagePreview GenerateThumbnail(string source, ImagePreviewOptions options)
    {
        using var decoded = Decode(source, options.ThumbMaxEdge);
        return CompressThumbnail(decoded, options);
    }

    /// <summary>一次解码同时产出预览图与缩略图（大图的解码占了绝大部分耗时，两份输出共用一次）。</summary>
    /// <param name="source">原始文件路径。</param>
    /// <param name="options">选项。</param>
    public static (GeneratedImagePreview Preview, GeneratedImagePreview Thumbnail) GenerateBoth(string source, ImagePreviewOptions options)
    {
        // JPEG 的按比例解码要同时满足两份输出：取较大的那条边（预览不限尺寸时不缩放）。
        var decodeEdge = options.MaxEdge > 0 ? Math.Max(options.MaxEdge, options.ThumbMaxEdge) : 0;
        using var decoded = Decode(source, decodeEdge);
        var (preview, rendered) = Compress(decoded.Image, decoded.Origin, decoded, options.MaxEdge, options.StartQuality, options.MinQuality, options.TargetBytes);
        using (rendered)
        {
            // 缩略图从已缩小的预览尺寸图再缩（便宜），而不是再从 5120² 的原图缩一遍。
            var (thumbnail, thumbnailImage) = Compress(rendered, SKEncodedOrigin.TopLeft, decoded, options.ThumbMaxEdge, options.ThumbQuality,
                Math.Min(options.ThumbQuality, 50), options.ThumbTargetBytes);
            thumbnailImage.Dispose();
            return (preview, thumbnail);
        }
    }

    private static GeneratedImagePreview CompressPreview(DecodedImage decoded, ImagePreviewOptions options)
    {
        var (result, rendered) = Compress(decoded.Image, decoded.Origin, decoded, options.MaxEdge, options.StartQuality, options.MinQuality, options.TargetBytes);
        rendered.Dispose();
        return result;
    }

    private static GeneratedImagePreview CompressThumbnail(DecodedImage decoded, ImagePreviewOptions options)
    {
        var (result, rendered) = Compress(decoded.Image, decoded.Origin, decoded, options.ThumbMaxEdge, options.ThumbQuality, Math.Min(options.ThumbQuality, 50), options.ThumbTargetBytes);
        rendered.Dispose();
        return result;
    }

    /// <summary>解码结果：位图、与位图共享像素的只读图像、EXIF 方向与原始字节数。</summary>
    private sealed class DecodedImage : IDisposable
    {
        public DecodedImage(SKBitmap bitmap, SKEncodedOrigin origin, long originalBytes, string name)
        {
            // 标记只读后 SKImage.FromBitmap 与位图共享像素；否则 Skia 会把 105 MB 的位图整份复制一遍（旧实现每次编码尝试都复制一次）。
            bitmap.SetImmutable();
            Bitmap = bitmap;
            Image = SKImage.FromBitmap(bitmap);
            Origin = origin;
            OriginalBytes = originalBytes;
            Name = name;
        }

        public SKBitmap Bitmap { get; }
        public SKImage Image { get; }
        public SKEncodedOrigin Origin { get; }
        public long OriginalBytes { get; }
        public string Name { get; }

        public void Dispose()
        {
            Image.Dispose();
            Bitmap.Dispose();
        }
    }

    private static DecodedImage Decode(string source, int maxEdge)
    {
        var original = new FileInfo(source).Length;
        using var codec = SKCodec.Create(source) ?? throw new InvalidDataException("无法解码图片：" + Path.GetFileName(source));
        var origin = codec.EncodedOrigin;
        // JPEG 支持按比例解码：直接解到目标尺寸附近，比先解 5120² 再缩快数倍（BMP 不支持，只能全解）
        var decodeInfo = codec.Info;
        if (maxEdge > 0 && Math.Max(decodeInfo.Width, decodeInfo.Height) > maxEdge && IsScalableCodec(source))
        {
            var scaled = codec.GetScaledDimensions(Math.Min(1f, maxEdge / (float)Math.Max(decodeInfo.Width, decodeInfo.Height)));
            decodeInfo = decodeInfo.WithSize(scaled);
        }
        var bitmap = SKBitmap.Decode(codec, decodeInfo) ?? throw new InvalidDataException("无法解码图片：" + Path.GetFileName(source));
        return new DecodedImage(bitmap, origin, original, Path.GetFileName(source));
    }

    /// <summary>
    /// 按「尺寸 + 体积」双约束编码成 JPEG：先降质量，质量到底再缩尺寸。
    /// 大图只缩放一次（5120² → 最终尺寸，含 mipmap 的代价约 0.2 秒），之后每次质量试探都只在这张小图上重新编码；
    /// 旧实现每次试探都从 75 MB 的原图重新缩放并复制一份位图，一张图要 1.5 秒以上。
    /// 返回的 <c>Rendered</c> 是最终尺寸的未压缩图像，调用方可以拿它继续生成更小的缩略图（也必须由调用方释放）。
    /// </summary>
    private static (GeneratedImagePreview Result, SKImage Rendered) Compress(SKImage source, SKEncodedOrigin origin, DecodedImage decoded,
        int maxEdge, int startQuality, int minQualityOption, long targetBytes)
    {
        // 先按最长边限制算目标尺寸（EXIF 旋转 90/270 时宽高互换）
        var swapped = origin is SKEncodedOrigin.LeftTop or SKEncodedOrigin.RightTop or SKEncodedOrigin.RightBottom or SKEncodedOrigin.LeftBottom;
        var sourceWidth = swapped ? source.Height : source.Width;
        var sourceHeight = swapped ? source.Width : source.Height;
        var scale = maxEdge > 0 && Math.Max(sourceWidth, sourceHeight) > maxEdge
            ? maxEdge / (double)Math.Max(sourceWidth, sourceHeight)
            : 1d;

        var quality = Math.Clamp(startQuality, 40, 100);
        var minQuality = Math.Clamp(minQualityOption, 30, quality);
        byte[]? best = null;
        var bestQuality = quality;
        var bestWidth = 0;
        var bestHeight = 0;
        var width = Math.Max(1, (int)Math.Round(sourceWidth * scale));
        var height = Math.Max(1, (int)Math.Round(sourceHeight * scale));
        var current = Render(source, origin, width, height);
        try
        {
            for (var attempt = 0; attempt < 8; attempt++)
            {
                var data = EncodeJpeg(current, quality);
                if (best is null || data.LongLength < best.LongLength)
                {
                    best = data;
                    bestQuality = quality;
                    bestWidth = width;
                    bestHeight = height;
                }
                if (data.LongLength <= targetBytes && quality <= startQuality) { return (Result(data, width, height, quality, decoded.OriginalBytes), current); }
                // 还能降质量就继续降：不允许缩尺寸（maxEdge=0）时用硬下限 40，尽量满足体积预算
                var floor = maxEdge > 0 ? minQuality : Math.Min(minQuality, 40);
                if (quality > floor) { quality = Math.Max(floor, quality - 5); continue; }
                if (maxEdge > 0 && width > maxEdge / 2)   // 质量到底了还不达标 → 继续缩尺寸（从已缩小的图再缩，很便宜）
                {
                    scale *= 0.85;
                    width = Math.Max(1, (int)Math.Round(sourceWidth * scale));
                    height = Math.Max(1, (int)Math.Round(sourceHeight * scale));
                    var smaller = Render(current, SKEncodedOrigin.TopLeft, width, height);
                    current.Dispose();
                    current = smaller;
                    continue;
                }
                break;
            }

            var fallback = best ?? throw new InvalidDataException("图片压缩失败：" + decoded.Name);
            var note = fallback.LongLength > targetBytes
                ? $"已尽量压缩到 {Format(fallback.LongLength)}（超过 {Format(targetBytes)} 目标）"
                : null;
            return (Result(fallback, bestWidth, bestHeight, bestQuality, decoded.OriginalBytes) with { Note = note }, current);
        }
        catch
        {
            current.Dispose();
            throw;
        }
    }

    private static GeneratedImagePreview Result(byte[] data, int width, int height, int quality, long original)
        => new(data, width, height, quality, original, $"已优化：{Format(original)} → {Format(data.LongLength)}（{width}×{height}，JPEG q{quality}）");

    /// <summary>该格式是否支持按比例解码（JPEG 支持；BMP/PNG 只能全尺寸解码）。</summary>
    private static bool IsScalableCodec(string path)
    {
        var extension = Path.GetExtension(path);
        return extension.Equals(".jpg", StringComparison.OrdinalIgnoreCase) || extension.Equals(".jpeg", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>按 EXIF 方向缩放到目标尺寸，得到不透明的 RGB 图像（照片方向正确，且不留透明通道）。</summary>
    private static SKImage Render(SKImage source, SKEncodedOrigin origin, int width, int height)
    {
        var info = new SKImageInfo(width, height, SKColorType.Rgb888x, SKAlphaType.Opaque);
        using var surface = SKSurface.Create(info);
        surface.Canvas.Clear(SKColors.White);
        surface.Canvas.Save();
        ApplyOrigin(surface.Canvas, origin, width, height);
        var sampling = new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear);
        var swapsAxes = origin is SKEncodedOrigin.LeftTop or SKEncodedOrigin.RightTop or SKEncodedOrigin.RightBottom or SKEncodedOrigin.LeftBottom;
        var drawWidth = swapsAxes ? height : width;
        var drawHeight = swapsAxes ? width : height;
        surface.Canvas.DrawImage(source, new SKRect(0, 0, drawWidth, drawHeight), sampling);
        surface.Canvas.Restore();
        return surface.Snapshot();
    }

    private static byte[] EncodeJpeg(SKImage image, int quality)
    {
        using var encoded = image.Encode(SKEncodedImageFormat.Jpeg, quality) ?? throw new InvalidDataException("JPEG 编码失败。");
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
                canvas.RotateDegrees(90);
                canvas.Scale(-1, 1);
                break;
            case SKEncodedOrigin.LeftBottom:
                canvas.Translate(0, height);
                canvas.RotateDegrees(-90);
                break;
        }
    }

    /// <summary>把字节数格式化为便于阅读的文本。</summary>
    public static string Format(long bytes) => bytes >= 1024L * 1024 * 1024
        ? (bytes / (1024d * 1024 * 1024)).ToString("0.##") + " GB"
        : bytes >= 1024 * 1024 ? (bytes / (1024d * 1024)).ToString("0.#") + " MB" : (bytes / 1024d).ToString("0.#") + " KB";
}
