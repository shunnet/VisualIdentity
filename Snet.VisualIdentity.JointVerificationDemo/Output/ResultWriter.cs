using SkiaSharp;
using Snet.Yolo.Server.anomalib;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Snet.VisualIdentity.JointVerificationDemo.Output;

/// <summary>只负责绘图和保存，不重新识别；视频平台可单独复用 Render 在内存中绘制。</summary>
public sealed class ResultWriter : IDisposable
{
    /// <summary>可覆盖中文缺陷名称的字体，由本对象统一释放。</summary>
    private readonly SKTypeface _typeface;

    /// <summary>创建输出绘制器；Linux 可指定 Noto Sans CJK 等中文字体。</summary>
    /// <param name="fontPath">字体文件路径；为空时使用系统字体匹配。</param>
    public ResultWriter(string? fontPath = null)
    {
        _typeface = fontPath is null ? SKTypeface.FromFamilyName("Microsoft YaHei")
            : SKTypeface.FromFile(fontPath) ?? throw new InvalidDataException("无法读取配置中的字体。");
    }

    /// <summary>复制原图后绘制结果，原图不变；返回的位图由调用方释放。</summary>
    /// <param name="source">原图位图。</param>
    /// <param name="output">联合验证或单模型识别结果。</param>
    /// <returns>与原图尺寸一致的结果图。</returns>
    public SKBitmap Render(SKBitmap source, JointValidationOutput output)
    {
        var image = source.Copy() ?? throw new InvalidOperationException("无法创建结果图。");
        try
        {
            using var canvas = new SKCanvas(image);
            using var font = new SKFont(_typeface, Math.Clamp(source.Width / 50f, 14, 36));
            using var pen = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = Math.Clamp(source.Width / 350f, 2, 6) };
            using var text = new SKPaint { IsAntialias = true, Color = SKColors.White };
            using var background = new SKPaint { Color = new SKColor(15, 23, 42, 220) };
            foreach (var match in output.Matches.Regions)
            {
                pen.Color = SKColors.Red;
                var label = match.Detections.Count == 0
                    ? (output.Mode == JointValidationMode.Joint ? $"区域 {match.Region.RegionId} · 类型未识别" : $"异常区域 {match.Region.RegionId}")
                    : $"异常区域 {match.Region.RegionId}";
                DrawBox(canvas, match.Region.Bounds, label, pen, font, text, background);
                foreach (var detection in match.Detections)
                {
                    pen.Color = SKColors.DodgerBlue;
                    DrawBox(canvas, detection.Bounds, $"{detection.Name} {detection.Confidence:P0}", pen, font, text, background);
                }
            }
            foreach (var detection in output.Matches.UnmatchedDetections)
            {
                pen.Color = SKColors.DodgerBlue;
                var suffix = output.Mode == JointValidationMode.Joint ? " · 异常区域外" : string.Empty;
                DrawBox(canvas, detection.Bounds, $"{detection.Name} {detection.Confidence:P0}{suffix}", pen, font, text, background);
            }
            if (output.Anomalib is { Result.IsAnomalous: false })
            {
                canvas.DrawText(output.Mode == JointValidationMode.Joint ? "正常 · 已跳过 YOLO" : "正常", 8, font.Size + 8, SKTextAlign.Left, font, text);
            }
            return image;
        }
        catch { image.Dispose(); throw; }
    }

    /// <summary>为一次识别生成独立目录，保存标注 PNG、热力图 PNG 和结构化 JSON，不覆盖历史结果。</summary>
    /// <param name="imagePath">待识别图片路径。</param>
    /// <param name="output">已经完成的识别结果。</param>
    /// <param name="elapsedMilliseconds">整体耗时，包含读取、预处理、冷启动与模型计算，不含写盘。</param>
    /// <param name="token">文件写入取消标记。</param>
    /// <returns>位于程序集 result 文件夹下的本次输出目录。</returns>
    public async Task<string> SaveAsync(string imagePath, JointValidationOutput output, long elapsedMilliseconds, CancellationToken token = default)
    {
        var directory = Path.Combine(AppContext.BaseDirectory, "result", DateTime.Now.ToString("yyyyMMdd-HHmmss-fff") + "-" + Guid.NewGuid().ToString("N"));
        var temporary = directory + ".tmp";
        Directory.CreateDirectory(temporary);
        try
        {
            using var source = SKBitmap.Decode(imagePath) ?? throw new InvalidDataException("无法解码待绘制图片。");
            using var rendered = Render(source, output);
            using var png = rendered.Encode(SKEncodedImageFormat.Png, 100) ?? throw new InvalidOperationException("结果图编码失败。");
            await File.WriteAllBytesAsync(Path.Combine(temporary, "annotated.png"), png.ToArray(), token);
            if (output.Anomalib is { HeatmapDataUrl.Length: > 0 } anomalib)
            {
                var encoded = anomalib.HeatmapDataUrl[(anomalib.HeatmapDataUrl.IndexOf(',') + 1)..];
                await File.WriteAllBytesAsync(Path.Combine(temporary, "heatmap.png"), Convert.FromBase64String(encoded), token);
            }
            // 热图单独保存，JSON 不重复写入 base64；后续平台可直接读取原图坐标、类别和分数。
            await using (var stream = File.Create(Path.Combine(temporary, "result.json")))
            {
                await JsonSerializer.SerializeAsync(stream, new
                {
                    ImagePath = imagePath,
                    output.Mode,
                    output.YoloExecuted,
                    output.YoloMilliseconds,
                    TotalMilliseconds = elapsedMilliseconds,
                    Anomalib = output.Anomalib?.Result,
                    output.Matches,
                }, new JsonSerializerOptions { WriteIndented = true, Converters = { new JsonStringEnumConverter() } }, token);
            }
            token.ThrowIfCancellationRequested();
            // 整套输出完成后一次性发布目录，取消或写入失败不会留下一套看似完整的结果。
            Directory.Move(temporary, directory);
            return directory;
        }
        finally { if (Directory.Exists(temporary)) { Directory.Delete(temporary, recursive: true); } }
    }

    /// <summary>绘制原图坐标框及标签；绘图层不改变阈值、区域或模型判定。</summary>
    /// <param name="canvas">结果图画布。</param>
    /// <param name="bounds">原图像素边界。</param>
    /// <param name="label">区域或缺陷说明。</param>
    /// <param name="pen">边框画笔。</param>
    /// <param name="font">标签字体。</param>
    /// <param name="text">文字画笔。</param>
    /// <param name="background">标签背景画笔。</param>
    private static void DrawBox(SKCanvas canvas, PixelRectangle bounds, string label, SKPaint pen, SKFont font, SKPaint text, SKPaint background)
    {
        canvas.DrawRect(new SKRect(bounds.X, bounds.Y, bounds.Right, bounds.Bottom), pen);
        var baseline = Math.Max(font.Size + 4, bounds.Y - 4);
        var width = font.MeasureText(label, text) + 8;
        canvas.DrawRect(bounds.X, baseline - font.Size - 3, width, font.Size + 7, background);
        canvas.DrawText(label, bounds.X + 4, baseline, SKTextAlign.Left, font, text);
    }

    /// <summary>释放绘图字体；应在全部绘图任务完成后调用。</summary>
    public void Dispose() => _typeface.Dispose();
}
