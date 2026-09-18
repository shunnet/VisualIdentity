using Snet.Yolo.Tasks.Services;
using SkiaSharp;
using Xunit;

namespace Snet.Yolo.Test;

/// <summary>
/// 验证页预览图：原图**不压缩**，按需生成一张「最长边 ≤ MaxEdge 且 ≤ 目标体积」的小预览，
/// 页面列表与主视图只加载预览，浏览器不必解码 5120×5120 大位图。
/// </summary>
public sealed class ValidationPreviewTests
{
    private static string NewDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "snet-valimage", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    /// <summary>造一张有细节的图（纯色图压缩率过高，测不出真实体积）。</summary>
    private static void WriteNoiseImage(string path, int width, int height, SKEncodedImageFormat format, int quality = 100)
    {
        using var bitmap = new SKBitmap(width, height);
        var random = new Random(20260918);
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                bitmap.SetPixel(x, y, new SKColor((byte)random.Next(256), (byte)random.Next(256), (byte)random.Next(256)));
            }
        }
        using var image = SKImage.FromBitmap(bitmap);
        if (format == SKEncodedImageFormat.Bmp)
        {
            WriteBmp(path, bitmap);            // SkiaSharp 不提供 BMP 编码，自己写一个最小实现
            return;
        }
        using var data = image.Encode(format, quality);
        using var stream = File.Create(path);
        data.SaveTo(stream);
    }

    /// <summary>把位图写成 24 位 BMP（现场数据就是这种格式，用来验证真实体积）。</summary>
    private static void WriteBmp(string path, SKBitmap bitmap)
    {
        var rowSize = (bitmap.Width * 3 + 3) / 4 * 4;
        var pixelBytes = rowSize * bitmap.Height;
        using var stream = new FileStream(path, FileMode.Create, FileAccess.Write);
        using var writer = new BinaryWriter(stream);
        writer.Write((byte)'B'); writer.Write((byte)'M');
        writer.Write(54 + pixelBytes);
        writer.Write(0);
        writer.Write(54);
        writer.Write(40);
        writer.Write(bitmap.Width);
        writer.Write(bitmap.Height);
        writer.Write((short)1);
        writer.Write((short)24);
        writer.Write(0);
        writer.Write(pixelBytes);
        writer.Write(2835); writer.Write(2835);
        writer.Write(0); writer.Write(0);
        var row = new byte[rowSize];
        for (var y = bitmap.Height - 1; y >= 0; y--)          // BMP 自下而上
        {
            for (var x = 0; x < bitmap.Width; x++)
            {
                var color = bitmap.GetPixel(x, y);
                row[x * 3] = color.Blue;
                row[x * 3 + 1] = color.Green;
                row[x * 3 + 2] = color.Red;
            }
            writer.Write(row);
        }
    }

    [Fact]
    public void BigBmp_IsDownscaledAndCompressedUnderBudget()
    {
        var root = NewDirectory();
        try
        {
            var source = Path.Combine(root, "现场图.bmp");
            WriteNoiseImage(source, 1600, 1200, SKEncodedImageFormat.Bmp);
            var originalBytes = new FileInfo(source).Length;
            Assert.True(originalBytes > 3_000_000, "测试样本应足够大，实际 " + originalBytes);

            var options = new ValidationPreviewOptions { TargetBytes = 200_000, MaxEdge = 1024 };
            Assert.True(ValidationPreviewGenerator.ShouldGenerate(source, originalBytes, 1600, 1200, options));

            var result = ValidationPreviewGenerator.Generate(source, options);

            Assert.True(result.Bytes <= options.TargetBytes, $"压缩后 {result.Bytes} 应不超过 {options.TargetBytes}");
            Assert.True(result.Width <= options.MaxEdge && result.Height <= options.MaxEdge, $"尺寸 {result.Width}×{result.Height} 应不超过最长边 {options.MaxEdge}");
            Assert.Equal(1600d / 1200d, result.Width / (double)result.Height, 2);          // 等比
            Assert.Contains("已优化", result.Note, StringComparison.Ordinal);

            // 产物确实是可解码的 JPEG
            using var decoded = SKBitmap.Decode(result.Data);
            Assert.NotNull(decoded);
            Assert.Equal(result.Width, decoded!.Width);
            Assert.Equal(result.Height, decoded.Height);
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public void LargeJpeg_KeepsResolutionWhenOnlySizeIsTooBig()
    {
        var root = NewDirectory();
        try
        {
            var source = Path.Combine(root, "big.jpg");
            WriteNoiseImage(source, 1200, 900, SKEncodedImageFormat.Jpeg);
            var originalBytes = new FileInfo(source).Length;

            // 预算很小但允许保持尺寸：应当主要靠降质量达标，而不是先缩尺寸
            var options = new ValidationPreviewOptions { TargetBytes = 600_000, MaxEdge = 0 };
            var result = ValidationPreviewGenerator.Generate(source, options);

            Assert.True(result.Bytes <= options.TargetBytes, $"{result.Bytes} 应 ≤ {options.TargetBytes}");
            Assert.Equal(1200, result.Width);
            Assert.Equal(900, result.Height);
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public void SmallJpeg_IsLeftAlone()
    {
        var root = NewDirectory();
        try
        {
            var source = Path.Combine(root, "small.jpg");
            WriteNoiseImage(source, 320, 240, SKEncodedImageFormat.Jpeg, 70);
            var bytes = new FileInfo(source).Length;

            var options = new ValidationPreviewOptions();
            Assert.False(ValidationPreviewGenerator.ShouldGenerate(source, bytes, 320, 240, options));
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public void BmpIsAlwaysConvertedEvenWhenSmall()
    {
        var root = NewDirectory();
        try
        {
            var source = Path.Combine(root, "small.bmp");
            WriteNoiseImage(source, 200, 150, SKEncodedImageFormat.Bmp);
            var bytes = new FileInfo(source).Length;

            // BMP 即使不大也值得转 JPEG：浏览器解码更省内存
            Assert.True(ValidationPreviewGenerator.ShouldGenerate(source, bytes, 200, 150, new ValidationPreviewOptions()));
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public void DisabledOption_NeverOptimizes()
    {
        var root = NewDirectory();
        try
        {
            var source = Path.Combine(root, "x.bmp");
            WriteNoiseImage(source, 400, 300, SKEncodedImageFormat.Bmp);
            Assert.False(ValidationPreviewGenerator.ShouldGenerate(source, 9_000_000, 5000, 5000, new ValidationPreviewOptions { Enabled = false }));
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public void BrokenFile_ThrowsInvalidDataInsteadOfCrashing()
    {
        var root = NewDirectory();
        try
        {
            var source = Path.Combine(root, "broken.jpg");
            File.WriteAllBytes(source, new byte[] { 1, 2, 3, 4, 5 });
            Assert.Throws<InvalidDataException>(() => ValidationPreviewGenerator.Generate(source, new ValidationPreviewOptions()));
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public async Task Store_GeneratesOnceAndReusesCache()
    {
        var root = NewDirectory();
        try
        {
            var source = Path.Combine(root, "big.bmp");
            WriteNoiseImage(source, 900, 700, SKEncodedImageFormat.Bmp);
            var options = new ValidationPreviewOptions { MaxEdge = 400, TargetBytes = 90_000 };
            var lifetime = new ValidationFileLifetime();
            var store = new ValidationPreviewStore(options, lifetime, Microsoft.Extensions.Logging.Abstractions.NullLogger<ValidationPreviewStore>.Instance);

            var first = await store.GetOrCreateAsync(source);
            Assert.NotNull(first);
            Assert.True(File.Exists(first));
            Assert.EndsWith(".preview.jpg", first, StringComparison.OrdinalIgnoreCase);
            Assert.True(new FileInfo(source).Length > 0);                        // 原图保持不变
            var previewBytes = new FileInfo(first!).Length;
            Assert.True(previewBytes <= options.TargetBytes, $"{previewBytes} 应 ≤ {options.TargetBytes}");

            var stamp = File.GetLastWriteTimeUtc(first!);
            var second = await store.GetOrCreateAsync(source);
            Assert.Equal(first, second);
            Assert.Equal(stamp, File.GetLastWriteTimeUtc(second!));               // 命中缓存，没有重新生成

            // 删除原图时预览一起删除
            store.Delete(source);
            Assert.False(File.Exists(first));
            lifetime.Dispose();
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public async Task Store_DisabledOrMissingFile_ReturnsNull()
    {
        var root = NewDirectory();
        try
        {
            var source = Path.Combine(root, "x.bmp");
            WriteNoiseImage(source, 300, 200, SKEncodedImageFormat.Bmp);
            var logger = Microsoft.Extensions.Logging.Abstractions.NullLogger<ValidationPreviewStore>.Instance;
            using var lifetime = new ValidationFileLifetime();

            var disabled = new ValidationPreviewStore(new ValidationPreviewOptions { Enabled = false }, lifetime, logger);
            Assert.Null(await disabled.GetOrCreateAsync(source));

            var enabled = new ValidationPreviewStore(new ValidationPreviewOptions(), lifetime, logger);
            Assert.Null(await enabled.GetOrCreateAsync(Path.Combine(root, "missing.bmp")));
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public async Task Store_ConcurrentRequests_DecodeTheImageOnlyOnce()
    {
        var root = NewDirectory();
        try
        {
            var source = Path.Combine(root, "concurrent.bmp");
            WriteNoiseImage(source, 800, 600, SKEncodedImageFormat.Bmp);
            using var lifetime = new ValidationFileLifetime();
            var store = new ValidationPreviewStore(new ValidationPreviewOptions { MaxEdge = 400 }, lifetime, Microsoft.Extensions.Logging.Abstractions.NullLogger<ValidationPreviewStore>.Instance);

            var results = await Task.WhenAll(Enumerable.Range(0, 6).Select(_ => store.GetOrCreateAsync(source)));

            Assert.All(results, path => Assert.Equal(results[0], path));           // 都拿到同一个预览
            Assert.Single(Directory.GetFiles(root, "*.preview.jpg"));              // 只生成了一份
        }
        finally { Directory.Delete(root, true); }
    }
    [Fact]
    public void ImageDimensions_AreCarriedSoTheOverlayCanScaleBoxesToThePreview()
    {
        // 识别框坐标是"原图像素"空间，画布放的是预览图 → 原图尺寸必须一路带到前端，
        // 否则框会被画到画布外面（现场 5120 的图 + 1600 的预览，看不到任何框）。
        var root = NewDirectory();
        try
        {
            var source = Path.Combine(root, "dims.jpg");
            WriteNoiseImage(source, 640, 480, SKEncodedImageFormat.Jpeg);
            Assert.Equal((640, 480), UploadedFileValidator.ValidateImageAndGetDimensions(source));

            var state = new ValidationState();
            var image = state.AddImage("snet", 1, "dims.jpg", "/uploads/snet/validation/x.jpg", isVideo: false, contentType: "image/jpeg", width: 640, height: 480);

            Assert.Equal(640, image.Width);
            Assert.Equal(480, image.Height);
            Assert.Equal(640, state.GetModel("snet", 1).Images.Single().Width);   // 快照也要带上
        }
        finally { Directory.Delete(root, true); }
    }
    [Fact]
    public void Format_ReadsHumanFriendlySizes()
    {
        Assert.Equal("900 KB", ValidationPreviewGenerator.Format(900 * 1024));
        Assert.Equal("1.5 MB", ValidationPreviewGenerator.Format((long)(1.5 * 1024 * 1024)));
        Assert.Equal("2 GB", ValidationPreviewGenerator.Format(2L * 1024 * 1024 * 1024));
    }
}
