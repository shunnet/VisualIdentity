using SkiaSharp;
using Snet.Yolo.Server.handler;
using Snet.Yolo.Server.models.data;
using Snet.Yolo.Tasks.Services;
using Xunit;
using YoloDotNet.Extensions;
using YoloDotNet.Models;

namespace Snet.Yolo.Test;

/// <summary>
/// 中文字体：视频结果帧由 SkiaSharp 绘制，默认字体（Windows 的 Segoe UI、Linux 的 DejaVu Sans）
/// <b>没有中文字形</b>，不显式指定字体时中文标签会被画成方框（tofu）。
/// 这里用像素级对比把"方框"与"真字形"区分开，并验证解析出的字体确实被 YoloDotNet 采纳。
/// </summary>
public sealed class MediaFontResolverTests
{
    private static MediaFontResolver NewResolver(out MediaToolSettingsStore store)
    {
        store = new MediaToolSettingsStore(Path.Combine(Path.GetTempPath(), "snet-media-fonts", Guid.NewGuid().ToString("N"), "media-tools.json"));
        return new MediaFontResolver(store);
    }

    /// <summary>把单个汉字画到白底上，返回编码后的 PNG 字节（用于逐像素对比）。</summary>
    private static byte[] RenderGlyph(SKTypeface typeface, string text)
    {
        using var bitmap = new SKBitmap(120, 80);
        using (var canvas = new SKCanvas(bitmap))
        {
            canvas.Clear(SKColors.White);
            using var font = new SKFont(typeface, 48);
            using var paint = new SKPaint { Color = SKColors.Black, IsAntialias = false };
            canvas.DrawText(text, 10, 60, SKTextAlign.Left, font, paint);
        }
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }

    /// <summary>渲染一张带中文标签的检测图，返回 PNG 字节（走与 ValidationService 相同的绘制路径）。</summary>
    private static byte[] RenderDetection(SKTypeface? typeface)
    {
        using var source = new SKBitmap(240, 140);
        using (var canvas = new SKCanvas(source)) { canvas.Clear(SKColors.White); }
        using var image = SKImage.FromBitmap(source);
        var detections = new List<ObjectDetectionResultData>
        {
            new() { Label = new LabelModel { Name = "纸" }, Confidence = 0.86, BoundingBox = new SKRectI(30, 40, 170, 110) },
        }.ToObjectDetection();
        using var drawn = typeface is null ? image.Draw(detections) : image.Draw(detections, new DetectionDrawingOptions { Font = typeface });
        using var data = drawn.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }

    /// <summary>根因：默认字体画任何汉字都是同一个方框——这正是用户看到的现象。</summary>
    [Fact]
    public void DefaultTypeface_DrawsDifferentChineseCharactersIdentically()
    {
        if (OperatingSystem.IsMacOS()) { return; }   // macOS 默认字体带中文，跳过
        Assert.False(MediaFontResolver.CoversChinese(SKTypeface.Default));

        var boxForFirst = RenderGlyph(SKTypeface.Default, "纸");
        var boxForSecond = RenderGlyph(SKTypeface.Default, "麻");

        Assert.Equal(boxForFirst, boxForSecond);
    }

    /// <summary>修复后：解析出的字体必须真的有中文字形，且不同汉字画出来不同。</summary>
    [Fact]
    public void ResolvedTypeface_CoversChineseGlyphs()
    {
        var resolver = NewResolver(out _);
        if (!resolver.HasCjkFont) { return; }   // 环境确实没有中文字体（例如未装字体的 Linux）

        var typeface = resolver.Resolve();
        Assert.True(MediaFontResolver.CoversChinese(typeface));
        Assert.NotEqual(RenderGlyph(typeface, "纸"), RenderGlyph(typeface, "麻"));
    }

    /// <summary>解析结果不能是"同样没有中文"的字体：要么有中文字形，要么明确标记为兜底。</summary>
    [Fact]
    public void Resolve_NeverPretendsToHaveChineseWhenItDoesNot()
    {
        var resolver = NewResolver(out _);

        var typeface = resolver.Resolve();

        Assert.Equal(resolver.HasCjkFont, MediaFontResolver.CoversChinese(typeface));
    }

    /// <summary>记录过的字体文件要优先生效（Linux 上装完字体后靠它绕过字体缓存）。</summary>
    [Fact]
    public void RecordedFontFile_TakesEffectAfterRecord()
    {
        var resolver = NewResolver(out var store);
        var known = MediaFontResolver.KnownFontFiles.FirstOrDefault(File.Exists);
        if (known is null) { return; }

        resolver.Record(known);

        Assert.True(resolver.HasCjkFont);
        Assert.Equal(known, resolver.ResolvedPath);
        Assert.Equal(known, store.Load().CjkFontPath);
    }

    /// <summary>关键回归：把字体传给 YoloDotNet 的绘制选项后，标签渲染结果必须与"默认字体"不同。</summary>
    [Fact]
    public void DetectionDrawing_UsesProvidedFontForChineseLabel()
    {
        var resolver = NewResolver(out _);
        if (!resolver.HasCjkFont) { return; }

        var withResolvedFont = RenderDetection(resolver.Resolve());
        var withDefaultFont = RenderDetection(null);

        Assert.NotEqual(withResolvedFont, withDefaultFont);
    }
}
