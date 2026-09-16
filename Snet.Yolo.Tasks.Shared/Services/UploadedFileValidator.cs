namespace Snet.Yolo.Tasks.Services;

using SkiaSharp;

/// <summary>校验用户上传文件的扩展名、实际编码和解码后尺寸。</summary>
public static class UploadedFileValidator
{
    /// <summary>单张图片允许上传的最大编码文件大小：100 MiB。</summary>
    public const long MaximumImageFileBytes = Snet.Yolo.Tasks.Core.Serialization.Import.YoloWithImagesImporter.MaximumImageBytes;

    private static readonly HashSet<string> ImageExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".jpg", ".jpeg", ".png", ".gif", ".webp", ".bmp",
    };

    private static readonly HashSet<string> VideoExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".mp4", ".webm", ".mov", ".avi", ".mkv",
    };

    /// <summary>最大允许解码像素数，防止压缩炸弹耗尽内存。</summary>
    public const long MaximumDecodedPixels = 100_000_000;

    /// <summary>返回规范化扩展名；不支持的类型会抛出异常。</summary>
    public static string GetExtension(string fileName, bool allowVideo)
    {
        var extension = Path.GetExtension(Path.GetFileName(fileName)).ToLowerInvariant();
        if (ImageExtensions.Contains(extension) || allowVideo && VideoExtensions.Contains(extension)) { return extension; }
        throw new InvalidDataException("不支持的文件类型: " + extension);
    }

    /// <summary>判断规范化扩展名是否属于视频类型。</summary>
    public static bool IsVideo(string extension) => VideoExtensions.Contains(extension);

    /// <summary>使用真实图片解码器校验编码和尺寸，而不是信任浏览器 Content-Type。</summary>
    public static void ValidateImage(string path)
        => _ = ValidateImageAndGetDimensions(path);

    /// <summary>使用真实图片解码器校验编码和尺寸，并返回图片像素尺寸。</summary>
    public static (int Width, int Height) ValidateImageAndGetDimensions(string path)
    {
        using var stream = File.OpenRead(path);
        using var codec = SKCodec.Create(stream) ?? throw new InvalidDataException("文件不是有效图片。");
        var info = codec.Info;
        if (info.Width <= 0 || info.Height <= 0 || (long)info.Width * info.Height > MaximumDecodedPixels)
        {
            throw new InvalidDataException($"图片尺寸无效或超过 {MaximumDecodedPixels:N0} 像素限制。");
        }
        return (info.Width, info.Height);
    }
}
