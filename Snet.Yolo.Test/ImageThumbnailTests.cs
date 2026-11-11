using Microsoft.Extensions.Logging.Abstractions;
using SkiaSharp;
using Snet.Yolo.Tasks.Services;
using Xunit;

namespace Snet.Yolo.Test;

/// <summary>
/// 列表/网格缩略图：页面缩略位只有几十到两百个 CSS 像素，必须用十几 KB 的小图，
/// 而不是 1600 像素的预览图（更不能是 75 MB 的原图）；并且派生图要持久（重启后不用重新解码原图）。
/// </summary>
public sealed class ImageThumbnailTests
{
    private static string NewDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "snet-thumb", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static ImagePreviewStore NewStore(ValidationFileLifetime lifetime, ImagePreviewOptions? options = null)
        => new(options ?? new ImagePreviewOptions(), lifetime, NullLogger<ImagePreviewStore>.Instance);

    /// <summary>造一张接近真实照片的图：平滑渐变 + 轻微噪声（纯噪声压不下去，纯色又压得过头）。</summary>
    private static void WriteImage(string path, int width, int height, SKEncodedImageFormat format)
    {
        using var bitmap = new SKBitmap(width, height);
        var random = new Random(7);
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var noise = random.Next(-6, 7);
                bitmap.SetPixel(x, y, new SKColor(
                    (byte)Math.Clamp(x * 255 / width + noise, 0, 255),
                    (byte)Math.Clamp(y * 255 / height + noise, 0, 255),
                    (byte)Math.Clamp((x + y) * 127 / (width + height) + 60 + noise, 0, 255)));
            }
        }
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(format, 90);
        using var stream = File.Create(path);
        data.SaveTo(stream);
    }

    private static (int Width, int Height) Dimensions(string path)
    {
        using var codec = SKCodec.Create(path)!;
        return (codec.Info.Width, codec.Info.Height);
    }

    [Fact]
    public void GenerateBoth_ThumbnailIsFarSmallerThanThePreview()
    {
        var root = NewDirectory();
        try
        {
            var source = Path.Combine(root, "big.png");
            WriteImage(source, 2400, 1800, SKEncodedImageFormat.Png);
            var options = new ImagePreviewOptions();

            var (preview, thumbnail) = ImagePreviewGenerator.GenerateBoth(source, options);

            Assert.True(Math.Max(preview.Width, preview.Height) <= options.MaxEdge);
            Assert.True(Math.Max(thumbnail.Width, thumbnail.Height) <= options.ThumbMaxEdge);
            Assert.True(thumbnail.Bytes <= options.ThumbTargetBytes, $"缩略图 {thumbnail.Bytes} 字节应 ≤ {options.ThumbTargetBytes}");
            Assert.True(thumbnail.Bytes * 3 < preview.Bytes, $"缩略图 {thumbnail.Bytes} 应远小于预览图 {preview.Bytes}");
            Assert.Equal(2400d / 1800d, thumbnail.Width / (double)thumbnail.Height, 2);   // 保持宽高比
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public async Task Store_ThumbnailRequest_AlsoProducesThePreviewFromTheSameDecode()
    {
        var root = NewDirectory();
        try
        {
            var source = Path.Combine(root, "scene.png");
            WriteImage(source, 1800, 1200, SKEncodedImageFormat.Png);
            using var lifetime = new ValidationFileLifetime();
            var store = NewStore(lifetime);

            var thumbnail = await store.GetOrCreateAsync(source, ImageVariant.Thumbnail, TestContext.Current.CancellationToken);

            Assert.Equal(ImagePreviewStore.ThumbPathFor(source), thumbnail);
            Assert.True(File.Exists(ImagePreviewStore.PathFor(source)), "首次解码原图时预览图一并产出，之后点开主视图不必再解码一次");
            var options = new ImagePreviewOptions();
            Assert.True(Math.Max(Dimensions(thumbnail!).Width, Dimensions(thumbnail!).Height) <= options.ThumbMaxEdge);
            Assert.True(new FileInfo(thumbnail!).Length <= options.ThumbTargetBytes);
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public async Task Store_ThumbnailIsCutFromTheExistingPreview_NotFromTheOriginalAgain()
    {
        var root = NewDirectory();
        try
        {
            var source = Path.Combine(root, "legacy.png");
            WriteImage(source, 1800, 1200, SKEncodedImageFormat.Png);
            using var lifetime = new ValidationFileLifetime();
            var store = NewStore(lifetime);
            await store.GetOrCreateAsync(source, ImageVariant.Preview, TestContext.Current.CancellationToken);
            File.Delete(ImagePreviewStore.ThumbPathFor(source));        // 模拟升级前的历史数据：只有预览，没有缩略图
            File.WriteAllText(source, "原图在这里不可读：缩略图必须直接从预览生成");

            var thumbnail = await store.GetOrCreateAsync(source, ImageVariant.Thumbnail, TestContext.Current.CancellationToken);

            Assert.Equal(ImagePreviewStore.ThumbPathFor(source), thumbnail);
            Assert.True(File.Exists(thumbnail));
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public async Task Store_TinyJpeg_IsUsedAsItsOwnThumbnail()
    {
        var root = NewDirectory();
        try
        {
            var source = Path.Combine(root, "tiny.jpg");
            WriteImage(source, 64, 48, SKEncodedImageFormat.Jpeg);
            Assert.True(new FileInfo(source).Length <= new ImagePreviewOptions().ThumbTargetBytes);
            using var lifetime = new ValidationFileLifetime();

            var thumbnail = await NewStore(lifetime).GetOrCreateAsync(source, ImageVariant.Thumbnail, TestContext.Current.CancellationToken);

            Assert.Equal(source, thumbnail);
            Assert.False(File.Exists(ImagePreviewStore.ThumbPathFor(source)));
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public async Task Store_DerivedFilesOfProjectImages_SurviveShutdown_ButValidationOnesAreCleaned()
    {
        var root = NewDirectory();
        try
        {
            var project = Path.Combine(root, "project.png");
            var validation = Path.Combine(root, "validation.png");
            WriteImage(project, 1200, 900, SKEncodedImageFormat.Png);
            WriteImage(validation, 1200, 900, SKEncodedImageFormat.Png);
            var lifetime = new ValidationFileLifetime();
            lifetime.Track(validation);                                 // 只有验证临时文件才登记为“随应用停止清理”
            var store = NewStore(lifetime);
            await store.GetOrCreateAsync(project, ImageVariant.Thumbnail, TestContext.Current.CancellationToken);
            await store.GetOrCreateAsync(validation, ImageVariant.Thumbnail, TestContext.Current.CancellationToken);

            lifetime.Dispose();                                         // 应用停止

            // 回归：以前所有预览都被登记，重启一次所有工程图片的预览就全没了，之后每张缩略图都要重新解码 75 MB 的原图。
            Assert.True(File.Exists(ImagePreviewStore.PathFor(project)));
            Assert.True(File.Exists(ImagePreviewStore.ThumbPathFor(project)));
            Assert.False(File.Exists(ImagePreviewStore.PathFor(validation)));
            Assert.False(File.Exists(ImagePreviewStore.ThumbPathFor(validation)));
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public async Task Store_Delete_RemovesPreviewAndThumbnail()
    {
        var root = NewDirectory();
        try
        {
            var source = Path.Combine(root, "gone.png");
            WriteImage(source, 1200, 900, SKEncodedImageFormat.Png);
            using var lifetime = new ValidationFileLifetime();
            var store = NewStore(lifetime);
            await store.GetOrCreateAsync(source, ImageVariant.Thumbnail, TestContext.Current.CancellationToken);

            store.Delete(source);

            Assert.All(ImagePreviewStore.DerivedPaths(source), path => Assert.False(File.Exists(path)));
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public async Task Store_ThumbnailGenerationFailure_ReturnsNullSoThePageShowsAPlaceholder()
    {
        var root = NewDirectory();
        try
        {
            var source = Path.Combine(root, "broken.png");
            File.WriteAllText(source, "不是图片");
            using var lifetime = new ValidationFileLifetime();

            var thumbnail = await NewStore(lifetime).GetOrCreateAsync(source, ImageVariant.Thumbnail, TestContext.Current.CancellationToken);

            Assert.Null(thumbnail);
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public async Task WarmUpMany_ProducesPreviewAndThumbnail_AndSkipsFinishedImages()
    {
        var root = NewDirectory();
        try
        {
            var first = Path.Combine(root, "a.png");
            var second = Path.Combine(root, "b.png");
            WriteImage(first, 1200, 900, SKEncodedImageFormat.Png);
            WriteImage(second, 1200, 900, SKEncodedImageFormat.Png);
            using var lifetime = new ValidationFileLifetime();
            var store = NewStore(lifetime);
            await store.GetOrCreateAsync(first, ImageVariant.Thumbnail, TestContext.Current.CancellationToken);
            Assert.True(store.IsComplete(first));
            Assert.False(store.IsComplete(second));

            store.WarmUpMany([first, second, Path.Combine(root, "missing.png")]);
            for (var i = 0; i < 100 && !store.IsComplete(second); i++) { await Task.Delay(100, TestContext.Current.CancellationToken); }

            Assert.True(File.Exists(ImagePreviewStore.PathFor(second)));
            Assert.True(File.Exists(ImagePreviewStore.ThumbPathFor(second)));
            Assert.False(File.Exists(ImagePreviewStore.ThumbPathFor(Path.Combine(root, "missing.png"))));
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public async Task Store_RespectsTheGlobalGenerationLimit()
    {
        // 并发上限 1：8 张图同时请求也只会一张一张解码，结果必须全部正确且互不覆盖。
        var root = NewDirectory();
        try
        {
            var files = Enumerable.Range(0, 8).Select(index => Path.Combine(root, $"img{index}.png")).ToList();
            foreach (var file in files) { WriteImage(file, 900, 600, SKEncodedImageFormat.Png); }
            using var lifetime = new ValidationFileLifetime();
            var store = NewStore(lifetime, new ImagePreviewOptions { MaxConcurrentGenerations = 1 });

            var results = await Task.WhenAll(files.Select(file => store.GetOrCreateAsync(file, ImageVariant.Thumbnail, TestContext.Current.CancellationToken)));

            Assert.Equal(files.Select(ImagePreviewStore.ThumbPathFor), results);
            Assert.All(results, path => Assert.True(File.Exists(path)));
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public void ImagePreviewUrl_ThumbFor_PointsAtTheThumbnailVariant()
    {
        Assert.Equal("/images/preview?scope=anomalib-validation&name=abc.bmp&size=thumb",
            ImagePreviewUrl.ThumbFor("/uploads/snet/anomalib-validation/abc.bmp"));
        Assert.Equal("/images/preview?scope=anomalib-validation&name=abc.bmp",
            ImagePreviewUrl.For("/uploads/snet/anomalib-validation/abc.bmp"));
        Assert.Equal(string.Empty, ImagePreviewUrl.ThumbFor(null));
        Assert.Equal("https://cdn.example/x.png", ImagePreviewUrl.ThumbFor("https://cdn.example/x.png"));   // 外部地址原样返回
    }

    [Fact]
    public void IsDerived_RecognisesPreviewThumbnailAndTemporaryFiles()
    {
        Assert.True(ImagePreviewStore.IsDerived("a.bmp.preview.jpg"));
        Assert.True(ImagePreviewStore.IsDerived("a.bmp.thumb.jpg"));
        Assert.True(ImagePreviewStore.IsDerived("a.bmp.thumb.jpg.tmp"));
        Assert.False(ImagePreviewStore.IsDerived("a.bmp"));
        Assert.False(ImagePreviewStore.IsDerived("photo.jpg"));
    }

    [Fact]
    public void WarmupService_FindsOriginalsOnly_NewestFirst()
    {
        var root = NewDirectory();
        try
        {
            var nested = Directory.CreateDirectory(Path.Combine(root, "snet", "project")).FullName;
            var older = Path.Combine(nested, "older.bmp");
            var newer = Path.Combine(nested, "newer.png");
            File.WriteAllText(older, "x");
            File.WriteAllText(newer, "x");
            File.SetLastWriteTimeUtc(older, DateTime.UtcNow.AddHours(-2));
            File.WriteAllText(newer + ".thumb.jpg", "x");
            File.WriteAllText(newer + ".preview.jpg", "x");
            File.WriteAllText(Path.Combine(nested, "result.mp4"), "x");
            File.WriteAllText(Path.Combine(nested, "notes.txt"), "x");

            var originals = ImagePreviewWarmupService.FindOriginals(root);

            Assert.Equal([newer, older], originals);
        }
        finally { Directory.Delete(root, true); }
    }
}
