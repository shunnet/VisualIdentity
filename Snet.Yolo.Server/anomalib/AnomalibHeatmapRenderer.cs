using SkiaSharp;

namespace Snet.Yolo.Server.anomalib;

/// <summary>
/// 把异常分数图渲染成透明红色热图。
///
/// 两点与旧实现不同：
///   · **绝对着色**：颜色强度按模型自带的判定阈值计算（低于“阈值 − 0.25”完全透明，阈值处约三分之一，满分最红），
///     而不是按“本张图的最小/最大值”拉伸。旧实现里正常图也会被拉成一片红色热点，让人误以为“热力图也显示异常”。
///   · **限制输出尺寸**：热图只是给人看的示意图，页面里最多显示约 256 个 CSS 像素。异常图常与模型输入同尺寸（1728、2048 像素时
///     约 300–850 毫秒的像素填充与 PNG 编码，data URL 达数 MB，还要经 Blazor 电路整段推给浏览器），这里最长边限制到 512。
/// </summary>
internal static class AnomalibHeatmapRenderer
{
    /// <summary>热图最长边上限（像素）。</summary>
    internal const int MaximumEdge = 512;

    /// <summary>阈值以下多宽的分数区间开始渐显（归一化分数单位）。</summary>
    internal const float RampBelowThreshold = 0.25f;

    /// <summary>渲染热图并编码为 PNG data URL。</summary>
    /// <param name="map">按行排列的归一化异常分数（0–1，阈值处约 0.5）。</param>
    /// <param name="width">异常图宽度。</param>
    /// <param name="height">异常图高度。</param>
    /// <param name="threshold">模型清单里的归一化判定阈值。</param>
    /// <returns>透明 PNG 的 data URL。</returns>
    internal static string Render(float[] map, int width, int height, float threshold)
    {
        var (pixels, outputWidth, outputHeight) = Rasterize(map, width, height, threshold);
        using var bitmap = new SKBitmap(new SKImageInfo(outputWidth, outputHeight, SKColorType.Rgba8888, SKAlphaType.Premul));
        bitmap.Pixels = pixels;
        // zlib 1 级 + Sub 滤波：比默认的 6 级 + 全部滤波快 8–10 倍（512×512 约 10 毫秒对 90 毫秒），体积只大 5–40%；
        // 热图是平滑的大片透明区域，本来就很好压缩。
        using var pixmap = bitmap.PeekPixels();
        using var encoded = pixmap.Encode(new SKPngEncoderOptions(SKPngEncoderFilterFlags.Sub, 1))
            ?? throw new InvalidDataException("热图 PNG 编码失败。");
        return "data:image/png;base64," + Convert.ToBase64String(encoded.ToArray());
    }

    /// <summary>按整数倍缩小（块内取最大值，几个像素的小缺陷缩小后也不会被稀释）并着色。</summary>
    /// <param name="map">按行排列的归一化异常分数。</param>
    /// <param name="width">异常图宽度。</param>
    /// <param name="height">异常图高度。</param>
    /// <param name="threshold">归一化判定阈值。</param>
    /// <returns>非预乘的像素、输出宽度与输出高度。</returns>
    internal static (SKColor[] Pixels, int Width, int Height) Rasterize(float[] map, int width, int height, float threshold)
    {
        var factor = Math.Max(1, (Math.Max(width, height) + MaximumEdge - 1) / MaximumEdge);
        var outputWidth = (width + factor - 1) / factor;
        var outputHeight = (height + factor - 1) / factor;
        var floor = Math.Max(0f, Math.Clamp(threshold, 0f, 1f) - RampBelowThreshold);
        var span = 1f - floor;
        var pixels = new SKColor[outputWidth * outputHeight];
        for (var oy = 0; oy < outputHeight; oy++)
        {
            var rowStart = oy * factor;
            var rowEnd = Math.Min(height, rowStart + factor);
            for (var ox = 0; ox < outputWidth; ox++)
            {
                var columnStart = ox * factor;
                var columnEnd = Math.Min(width, columnStart + factor);
                var peak = float.NegativeInfinity;
                for (var y = rowStart; y < rowEnd; y++)
                {
                    var offset = y * width;
                    for (var x = columnStart; x < columnEnd; x++) { peak = Math.Max(peak, map[offset + x]); }
                }
                var intensity = span <= 1e-6f ? 0f : Math.Clamp((peak - floor) / span, 0f, 1f);
                pixels[oy * outputWidth + ox] = new SKColor(255, (byte)(128 * (1 - intensity)), 0, (byte)(220 * intensity));
            }
        }
        return (pixels, outputWidth, outputHeight);
    }
}
