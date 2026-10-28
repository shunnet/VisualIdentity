using SkiaSharp;
using Snet.Yolo.Server.sam;
using Snet.Yolo.Tasks.Core.Editing;
using Snet.Yolo.Tasks.Core.Models;
using Snet.Yolo.Tasks.Core.Serialization;
using Xunit;

namespace Snet.Yolo.Test;

/// <summary>大图 SAM 预处理和原图坐标映射，不依赖下载模型。</summary>
public sealed class SamLargeImageTests
{
    /// <summary>一亿像素 JPEG 必须缩小解码，源文件保持不变。</summary>
    [Fact]
    public void DecodeImage_HundredMegapixelJpegUsesBoundedWorkingBitmap()
    {
        var path = Path.Combine(Path.GetTempPath(), "sam-large-" + Guid.NewGuid().ToString("N") + ".jpg");
        try
        {
            using (var bitmap = new SKBitmap(10000, 10000))
            {
                bitmap.Erase(SKColors.Red);
                using var encoded = bitmap.Encode(SKEncodedImageFormat.Jpeg, 85);
                using var file = File.Create(path);
                encoded.SaveTo(file);
            }
            var originalBytes = File.ReadAllBytes(path);
            using var codec = SKCodec.Create(path);

            using var working = SamOnnxRuntime.DecodeImage(codec);

            Assert.Equal(10000, codec.Info.Width);
            Assert.Equal(10000, codec.Info.Height);
            Assert.InRange(working.Width, 1, 1024);
            Assert.Equal(working.Width, working.Height);
            Assert.True(working.GetPixel(working.Width / 2, working.Height / 2).Red > 230);
            Assert.Equal(originalBytes, File.ReadAllBytes(path));
        }
        finally { File.Delete(path); }
    }

    /// <summary>生成大图标注不应分配整张一亿像素掩码，轮廓仍为原图坐标。</summary>
    [Fact]
    public void Geometry_HundredMegapixelCoordinatesWithoutFullSizeAllocation()
    {
        var mask = new byte[100];
        for (var y = 2; y < 8; y++) { for (var x = 3; x < 9; x++) { mask[y * 10 + x] = 255; } }
        var before = GC.GetAllocatedBytesForCurrentThread();

        var result = SamMaskGeometry.Create(mask, 10, 10, 10000, 10000, 4, 3, .9f, TestContext.Current.CancellationToken);

        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.True(allocated < 2_000_000, $"小工作掩码不应随一亿像素原图分配内存，实际分配 {allocated} 字节。");
        Assert.Equal(10000, result.Width);
        Assert.Equal(10000, result.Height);
        Assert.Equal(3000, result.X);
        Assert.Equal(2000, result.Y);
        Assert.Equal(6000, result.BoxWidth);
        Assert.Equal(6000, result.BoxHeight);
        Assert.All(result.PointsX, x => Assert.InRange(x, 3000, 9000));
        Assert.All(result.PointsY, y => Assert.InRange(y, 2000, 8000));
    }

    /// <summary>不支持原生缩小的格式也必须限制最终工作图，并保持颜色和宽高比。</summary>
    [Theory]
    [InlineData(SKEncodedImageFormat.Png)]
    [InlineData(SKEncodedImageFormat.Jpeg)]
    public void DecodeImage_LimitsWorkingSizeForEachFormat(SKEncodedImageFormat format)
    {
        using var bitmap = new SKBitmap(3072, 1536); bitmap.Erase(SKColors.Lime);
        using var encoded = bitmap.Encode(format, 90);
        using var codec = SKCodec.Create(encoded);
        using var working = SamOnnxRuntime.DecodeImage(codec);
        Assert.Equal(1024, working.Width); Assert.Equal(512, working.Height);
        Assert.True(working.GetPixel(512, 256).Green > 230);
    }

    /// <summary>缩小解码仍需应用 EXIF 旋转，坐标方向必须与浏览器一致。</summary>
    [Fact]
    public void DecodeImage_NormalizesExifAfterDownsampling()
    {
        using var bitmap = new SKBitmap(3072, 1536);
        using (var canvas = new SKCanvas(bitmap))
        {
            canvas.Clear(SKColors.Red);
            using var paint = new SKPaint { Color = SKColors.Blue };
            canvas.DrawRect(0, 768, 3072, 768, paint);
        }
        using var encoded = bitmap.Encode(SKEncodedImageFormat.Jpeg, 90);
        byte[] exif = [69,120,105,102,0,0,73,73,42,0,8,0,0,0,1,0,18,1,3,0,1,0,0,0,6,0,0,0,0,0,0,0];
        var jpeg = encoded.ToArray();
        using var stream = new MemoryStream();
        stream.Write(jpeg.AsSpan(0, 2)); stream.Write([255,225,0,34]); stream.Write(exif); stream.Write(jpeg.AsSpan(2));
        using var data = SKData.CreateCopy(stream.ToArray()); using var codec = SKCodec.Create(data);
        using var working = SamOnnxRuntime.DecodeImage(codec);
        Assert.Equal(SKEncodedOrigin.RightTop, codec.EncodedOrigin);
        Assert.Equal(512, working.Width); Assert.Equal(1024, working.Height);
        Assert.True(working.GetPixel(128, 256).Blue > 230);
        Assert.True(working.GetPixel(384, 256).Red > 230);
    }

    /// <summary>超过一亿像素仍在解码前拒绝；仅修改 JPEG 尺寸头，不分配超大位图。</summary>
    [Fact]
    public void DecodeImage_RejectsMoreThanHundredMegapixels()
    {
        using var bitmap = new SKBitmap(10, 10); bitmap.Erase(SKColors.Red);
        using var encoded = bitmap.Encode(SKEncodedImageFormat.Jpeg, 90);
        var bytes = encoded.ToArray(); var found = false;
        for (var i = 2; i + 8 < bytes.Length; i++)
        {
            if (bytes[i] != 255 || bytes[i + 1] != 192) { continue; }
            bytes[i + 5] = 10000 >> 8; bytes[i + 6] = 10000 & 255;
            bytes[i + 7] = 10001 >> 8; bytes[i + 8] = 10001 & 255; found = true; break;
        }
        Assert.True(found);
        using var data = SKData.CreateCopy(bytes); using var codec = SKCodec.Create(data);
        Assert.Equal(10001, codec.Info.Width); Assert.Equal(10000, codec.Info.Height);
        Assert.Contains("1 亿像素", Assert.Throws<InvalidDataException>(() => SamOnnxRuntime.DecodeImage(codec)).Message);
    }

    /// <summary>不同尺寸的紧凑掩码查询和兼容原图掩码必须一致，孔洞及边界不能偏移。</summary>
    [Theory]
    [InlineData(11, 7)]
    [InlineData(2, 2)]
    public void CompactMask_SamplingMatchesOriginalMask(int width, int height)
    {
        byte[] mask = [255,255,255,255,0,255,255,255,255];
        var result = SamMaskGeometry.Create(mask, 3, 3, width, height, 0, 0, .9f, TestContext.Current.CancellationToken);
        Assert.Equal(9, result.WorkingMask.Length);
        for (var y = 0; y < height; y++) for (var x = 0; x < width; x++)
        { Assert.Equal(mask[y * 3 / height * 3 + x * 3 / width] != 0, result.ContainsPoint(x, y)); }
        Assert.False(result.ContainsPoint(-1, 0)); Assert.False(result.ContainsPoint(width, height - 1));
        var expanded = result.Mask;
        for (var y = 0; y < height; y++) for (var x = 0; x < width; x++)
        { Assert.Equal(mask[y * 3 / height * 3 + x * 3 / width], expanded[y * width + x]); }
    }

    /// <summary>非整数放大、缩小与跨行的游程拼接必须生成原图尺寸的标准 RLE。</summary>
    [Theory]
    [InlineData(7, 5)]
    [InlineData(2, 1)]
    public void ScaledRle_UsesExactNearestNeighborPixels(int width, int height)
    {
        byte[] source = [0,255,0,255,255,0];
        byte[] expected = width == 7
            ? [0,0,0,255,255,0,0, 0,0,0,255,255,0,0, 0,0,0,255,255,0,0, 255,255,255,255,255,0,0, 255,255,255,255,255,0,0]
            : [0,255];
        var encoded = RleCodec.EncodeScaled(source, 3, 2, width, height);
        Assert.Equal(expected, RleCodec.Decode(encoded, width * height));
        Assert.Equal(RleCodec.Encode(expected), encoded);
    }

    /// <summary>大图 RLE 直接从工作图生成，不能预分配原图像素数组。</summary>
    [Fact]
    public void ScaledRle_HundredMegapixelMaskHasBoundedAllocation()
    {
        var before = GC.GetAllocatedBytesForCurrentThread();
        var encoded = RleCodec.EncodeScaled([255], 1, 1, 10000, 10000);
        Assert.True(GC.GetAllocatedBytesForCurrentThread() - before < 10_000_000);
        Assert.Equal(100_000_000, Enumerable.Range(0, encoded.Length / 2).Sum(i => (int)encoded[i * 2]));
        Assert.All(Enumerable.Range(0, encoded.Length / 2), i => Assert.Equal(255, encoded[i * 2 + 1]));
        Assert.Equal(255, encoded[0]); Assert.Equal(100_000_000 % 255, encoded[^2]);
    }

    /// <summary>错误尺寸不能进入缩放编码，避免溢出或越界。</summary>
    [Fact]
    public void ScaledRle_RejectsInvalidDimensions()
    {
        Assert.Throws<ArgumentException>(() => RleCodec.EncodeScaled([255], 2, 2, 10, 10));
        Assert.Throws<ArgumentException>(() => RleCodec.EncodeScaled([255], 1, 1, 0, 10));
        Assert.Throws<ArgumentException>(() => RleCodec.EncodeScaled([255], 1, 1, int.MaxValue, 2));
    }

    /// <summary>紧凑笔刷保存原图 RLE 和孔洞，并保持保存回显与撤销重做。</summary>
    [Fact]
    public void CompactBrush_PreservesOriginalRleHoleAndUndoRedo()
    {
        const string config = "<View><Image name='image' value='$image'/><BrushLabels name='brush' toName='image'><Label value='object'/></BrushLabels></View>";
        var task = new AnnotationTask(); var session = new LabelingSession(config, task); session.SetImageOriginalSize(11, 7);
        byte[] mask = [255,255,255,255,0,255,255,255,255];
        var result = SamMaskGeometry.Create(mask, 3, 3, 11, 7, 0, 0, .9f, TestContext.Current.CancellationToken);
        var row = session.AddFilledBrushMask(result.WorkingMask.Span, result.MaskWidth, result.MaskHeight, result.PointsX, result.PointsY, result.PreviewDataUrl, "object");
        var rle = row.Value!["rle"]!.AsArray().Select(n => (byte)n!.GetValue<int>()).ToArray();
        Assert.Equal(result.Mask, RleCodec.Decode(rle, 77));
        Assert.Equal(0, RleCodec.Decode(rle, 77)[3 * 11 + 5]);
        var restored = new LabelingSession(config, TaskJson.DeserializeTask(TaskJson.SerializeTask(task))!); restored.SetImageOriginalSize(11, 7);
        Assert.Equal(result.PreviewDataUrl, restored.BuildRegionViews()[0].MaskDataUrl);
        session.Undo(); Assert.Empty(session.BuildRegionViews()); session.Redo(); Assert.Single(session.BuildRegionViews());
    }
}
