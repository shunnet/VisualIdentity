using Snet.Yolo.Tasks.Services;
using SkiaSharp;
using Xunit;

namespace Snet.Yolo.Test;

/// <summary>
/// 验证页图片优化：现场大图（BMP/超大 JPEG）在上传时压到「最长边 ≤ MaxEdge 且 ≤ 5 MiB」，
/// 保证清晰度的同时让验证页不再卡顿。只作用于验证页，不影响标注与训练数据集的图片。
/// </summary>
public sealed class ValidationImageOptimizerTests
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

            var options = new ValidationImageOptions { TargetBytes = 700_000, MaxEdge = 1024 };
            Assert.True(ValidationImageOptimizer.ShouldOptimize(source, originalBytes, 1600, 1200, options));

            var result = ValidationImageOptimizer.Optimize(source, options);

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
            var options = new ValidationImageOptions { TargetBytes = 600_000, MaxEdge = 0 };
            var result = ValidationImageOptimizer.Optimize(source, options);

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

            var options = new ValidationImageOptions();
            Assert.False(ValidationImageOptimizer.ShouldOptimize(source, bytes, 320, 240, options));
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
            Assert.True(ValidationImageOptimizer.ShouldOptimize(source, bytes, 200, 150, new ValidationImageOptions()));
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
            Assert.False(ValidationImageOptimizer.ShouldOptimize(source, 9_000_000, 5000, 5000, new ValidationImageOptions { Enabled = false }));
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
            Assert.Throws<InvalidDataException>(() => ValidationImageOptimizer.Optimize(source, new ValidationImageOptions()));
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public void Format_ReadsHumanFriendlySizes()
    {
        Assert.Equal("900 KB", ValidationImageOptimizer.Format(900 * 1024));
        Assert.Equal("1.5 MB", ValidationImageOptimizer.Format((long)(1.5 * 1024 * 1024)));
        Assert.Equal("2 GB", ValidationImageOptimizer.Format(2L * 1024 * 1024 * 1024));
    }
}
