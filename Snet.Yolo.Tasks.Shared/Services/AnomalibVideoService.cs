namespace Snet.Yolo.Tasks.Services;

using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;
using SkiaSharp;
using Snet.Yolo.Server.Anomalib;

/// <summary>Anomalib 视频逐帧识别的阶段。</summary>
public enum AnomalibVideoStage
{
    /// <summary>正在从视频提取帧。</summary>
    Decoding,
    /// <summary>正在逐帧执行 ONNX 推理。</summary>
    Recognizing,
    /// <summary>正在编码标注结果视频。</summary>
    Encoding,
}

/// <summary>视频识别当前进度。</summary>
/// <param name="Stage">当前视频处理阶段。</param>
/// <param name="CompletedFrames">已完成识别的帧数。</param>
/// <param name="TotalFrames">本次视频帧总数。</param>
public sealed record AnomalibVideoProgress(AnomalibVideoStage Stage, int CompletedFrames, int TotalFrames);

/// <summary>已生成的标注视频及异常帧统计。</summary>
/// <param name="FileName">写入用户上传目录的结果视频文件名。</param>
/// <param name="TotalFrames">已识别的视频帧数。</param>
/// <param name="AnomalousFrames">被模型判定异常的视频帧数。</param>
public sealed record AnomalibVideoResult(string FileName, int TotalFrames, int AnomalousFrames);

/// <summary>复用 YOLO 的 FFmpeg 安装，逐帧执行 Anomalib 推理并编码可播放的标注视频。</summary>
public sealed class AnomalibVideoService(MediaToolResolver mediaTools, AnomalibInferenceService inference)
{
    /// <summary>上传后、开始识别前用真实视频流校验文件。</summary>
    public async Task ValidateAsync(string videoPath, CancellationToken cancellationToken)
        => _ = await ReadMetadataAsync(mediaTools.GetPaths().FFprobe, videoPath, cancellationToken);

    /// <summary>处理已校验并保存在当前用户上传目录内的视频。</summary>
    public async Task<AnomalibVideoResult> IdentifyAsync(
        RegisteredAnomalibModel model,
        string videoPath,
        Action<AnomalibVideoProgress> report,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(report);
        var tools = mediaTools.GetPaths();
        var info = await ReadMetadataAsync(tools.FFprobe, videoPath, cancellationToken);
        var estimatedFrames = info.Duration * info.FrameRate;
        if (estimatedFrames > 10_000) { throw new InvalidDataException("视频帧数超过 10000 帧限制。"); }
        report(new(AnomalibVideoStage.Decoding, 0, (int)Math.Ceiling(estimatedFrames)));

        var work = Path.Combine(Path.GetTempPath(), "visualidentity-anomalib-video", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(work);
        var outputName = "anomalib_" + Guid.NewGuid().ToString("N") + ".mp4";
        var outputPath = Path.Combine(Path.GetDirectoryName(videoPath)!, outputName);
        var sourcePattern = Path.Combine(work, "source_%09d.jpg");
        try
        {
            var extraction = await RunAsync(tools.FFmpeg, cancellationToken,
                "-hide_banner", "-loglevel", "error", "-i", videoPath, "-map", "0:v:0",
                "-fps_mode", "passthrough", "-frames:v", "10001", "-q:v", "3", "-y", sourcePattern);
            if (extraction.ExitCode != 0 && extraction.Error.Contains("fps_mode", StringComparison.OrdinalIgnoreCase))
            {
                extraction = await RunAsync(tools.FFmpeg, cancellationToken,
                    "-hide_banner", "-loglevel", "error", "-i", videoPath, "-map", "0:v:0",
                    "-vsync", "0", "-frames:v", "10001", "-q:v", "3", "-y", sourcePattern);
            }
            if (extraction.ExitCode != 0) { throw new InvalidOperationException("FFmpeg 视频抽帧失败：" + extraction.Error); }

            var frames = Directory.GetFiles(work, "source_*.jpg").OrderBy(static path => path, StringComparer.Ordinal).ToArray();
            if (frames.Length == 0 || frames.Length > 10_000) { throw new InvalidDataException("视频没有有效帧或帧数超过 10000。"); }
            var anomalousFrames = 0;
            for (var index = 0; index < frames.Length; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var result = await inference.IdentifyAsync(model, frames[index], cancellationToken, includeHeatmap: false);
                if (result.Result.IsAnomalous) { anomalousFrames++; }
                DrawFrame(frames[index], Path.Combine(work, $"result_{index + 1:000000000}.jpg"), result.Result);
                if ((index + 1) % Math.Max(1, frames.Length / 100) == 0 || index == frames.Length - 1)
                { report(new(AnomalibVideoStage.Recognizing, index + 1, frames.Length)); }
            }

            report(new(AnomalibVideoStage.Encoding, frames.Length, frames.Length));
            var timestamps = await ReadFrameTimestampsAsync(tools.FFprobe, videoPath, frames.Length, cancellationToken);
            var durations = FrameDurations(timestamps, info.FrameRate);
            var playlist = Path.Combine(work, "result.ffconcat");
            var playlistText = new StringBuilder("ffconcat version 1.0\n");
            for (var index = 0; index < frames.Length; index++)
            {
                playlistText.Append("file result_").Append((index + 1).ToString("000000000", CultureInfo.InvariantCulture)).Append(".jpg\n");
                playlistText.Append("duration ").Append(durations[index].ToString("0.#########", CultureInfo.InvariantCulture)).Append('\n');
            }
            playlistText.Append("file result_").Append(frames.Length.ToString("000000000", CultureInfo.InvariantCulture)).Append(".jpg\n");
            await File.WriteAllTextAsync(playlist, playlistText.ToString(), new UTF8Encoding(false), cancellationToken);
            var temporaryOutput = Path.Combine(work, "output.mp4");
            var encoded = await RunAsync(tools.FFmpeg, cancellationToken,
                "-hide_banner", "-loglevel", "error", "-f", "concat", "-safe", "0", "-i", playlist,
                "-i", videoPath, "-map", "0:v:0", "-map", "1:a?",
                "-frames:v", frames.Length.ToString(CultureInfo.InvariantCulture), "-c:v", "libx264", "-pix_fmt", "yuv420p", "-fps_mode", "vfr", "-c:a", "aac", "-movflags", "+faststart", "-y", temporaryOutput);
            if (encoded.ExitCode != 0 && encoded.Error.Contains("fps_mode", StringComparison.OrdinalIgnoreCase))
            {
                encoded = await RunAsync(tools.FFmpeg, cancellationToken,
                    "-hide_banner", "-loglevel", "error", "-f", "concat", "-safe", "0", "-i", playlist,
                    "-i", videoPath, "-map", "0:v:0", "-map", "1:a?",
                    "-frames:v", frames.Length.ToString(CultureInfo.InvariantCulture), "-c:v", "libx264", "-pix_fmt", "yuv420p", "-vsync", "vfr", "-c:a", "aac", "-movflags", "+faststart", "-y", temporaryOutput);
            }
            if (encoded.ExitCode != 0) { throw new InvalidOperationException("FFmpeg 结果视频编码失败：" + encoded.Error); }
            cancellationToken.ThrowIfCancellationRequested();
            File.Move(temporaryOutput, outputPath);
            return new(outputName, frames.Length, anomalousFrames);
        }
        finally
        {
            try { Directory.Delete(work, recursive: true); }
            catch (IOException) { /* 临时文件清理不得掩盖原始识别错误。 */ }
            catch (UnauthorizedAccessException) { /* 临时文件清理不得掩盖原始识别错误。 */ }
        }
    }

    /// <summary>在原帧像素坐标中绘制异常区域，保持输出帧尺寸不变。</summary>
    internal static void DrawFrame(string source, string destination, AnomalibImageResult result)
    {
        using var bitmap = SKBitmap.Decode(source) ?? throw new InvalidDataException("视频帧无法解码。");
        using var canvas = new SKCanvas(bitmap);
        using var paint = new SKPaint { Color = new SKColor(255, 91, 96), IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = Math.Max(2, bitmap.Width / 600f) };
        foreach (var region in result.Regions)
        {
            var bounds = region.Bounds;
            canvas.DrawRect(SKRect.Create(bounds.X, bounds.Y, bounds.Width, bounds.Height), paint);
        }
        using var image = SKImage.FromBitmap(bitmap);
        using var encoded = image.Encode(SKEncodedImageFormat.Jpeg, 88) ?? throw new InvalidDataException("无法编码标注帧。");
        using var stream = File.Create(destination);
        encoded.SaveTo(stream);
    }

    /// <summary>使用 FFprobe 校验真实视频流、时长、帧率及尺寸。</summary>
    private static async Task<(double Duration, double FrameRate)> ReadMetadataAsync(string ffprobe, string path, CancellationToken cancellationToken)
    {
        var probe = await RunAsync(ffprobe, cancellationToken, "-v", "error", "-select_streams", "v:0",
            "-show_entries", "stream=width,height,avg_frame_rate,r_frame_rate:format=duration", "-of", "json", path);
        if (probe.ExitCode != 0) { throw new InvalidDataException("无法读取视频信息：" + probe.Error); }
        using var document = JsonDocument.Parse(probe.Output);
        var root = document.RootElement;
        if (!root.TryGetProperty("streams", out var streams) || streams.GetArrayLength() == 0) { throw new InvalidDataException("视频中没有有效视频流。"); }
        var stream = streams[0];
        var width = stream.GetProperty("width").GetInt32();
        var height = stream.GetProperty("height").GetInt32();
        if (width <= 0 || height <= 0 || (long)width * height > UploadedFileValidator.MaximumDecodedPixels)
        { throw new InvalidDataException("视频帧尺寸无效或超过限制。"); }
        var duration = double.Parse(root.GetProperty("format").GetProperty("duration").GetString()!, CultureInfo.InvariantCulture);
        var rate = ParseFrameRate(stream.GetProperty("avg_frame_rate").GetString());
        if (rate <= 0 && stream.TryGetProperty("r_frame_rate", out var fallback)) { rate = ParseFrameRate(fallback.GetString()); }
        if (!double.IsFinite(duration) || !double.IsFinite(rate) || duration <= 0 || rate <= 0 || duration * rate > int.MaxValue)
        { throw new InvalidDataException("视频时长或帧率无效。"); }
        return (duration, rate);
    }

    /// <summary>解析 FFprobe 返回的分数帧率。</summary>
    internal static double ParseFrameRate(string? text)
    {
        var parts = (text ?? string.Empty).Split('/');
        if (!double.TryParse(parts[0], CultureInfo.InvariantCulture, out var numerator)) { return 0; }
        if (parts.Length == 1) { return numerator; }
        return double.TryParse(parts[1], CultureInfo.InvariantCulture, out var denominator) && denominator != 0 ? numerator / denominator : 0;
    }

    /// <summary>读取原视频每帧时间戳，确保标注帧重新编码后保留变帧率节奏。</summary>
    private static async Task<IReadOnlyList<double>> ReadFrameTimestampsAsync(string ffprobe, string path, int expectedCount, CancellationToken cancellationToken)
    {
        var probe = await RunAsync(ffprobe, cancellationToken, "-v", "error", "-select_streams", "v:0",
            "-show_entries", "frame=best_effort_timestamp_time", "-of", "csv=p=0", path);
        if (probe.ExitCode != 0) { throw new InvalidDataException("无法读取视频帧时间戳：" + probe.Error); }
        var values = probe.Output.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries);
        if (values.Length != expectedCount) { throw new InvalidDataException("视频帧时间戳数量与抽帧数量不一致。"); }
        var timestamps = new double[values.Length];
        for (var index = 0; index < values.Length; index++)
        {
            if (!double.TryParse(values[index].Trim(), CultureInfo.InvariantCulture, out timestamps[index]) || !double.IsFinite(timestamps[index]))
            { throw new InvalidDataException("视频包含无效帧时间戳。"); }
        }
        return timestamps;
    }

    /// <summary>把原始时间戳换算为 concat 输入各帧的播放时长，拒绝乱序时间戳。</summary>
    internal static double[] FrameDurations(IReadOnlyList<double> timestamps, double fallbackRate)
    {
        if (timestamps.Count == 0 || !double.IsFinite(fallbackRate) || fallbackRate <= 0)
        { throw new InvalidDataException("视频帧时间戳或帧率无效。"); }
        var durations = new double[timestamps.Count];
        for (var index = 0; index < timestamps.Count - 1; index++)
        {
            durations[index] = timestamps[index + 1] - timestamps[index];
            if (!double.IsFinite(durations[index]) || durations[index] <= 0)
            { throw new InvalidDataException("视频帧时间戳没有按顺序递增。"); }
        }
        durations[^1] = 1d / fallbackRate;
        return durations;
    }

    /// <summary>安全启动媒体工具并同时排空标准输出与错误流；取消时终止进程树。</summary>
    private static async Task<(int ExitCode, string Output, string Error)> RunAsync(string executable, CancellationToken cancellationToken, params string[] arguments)
    {
        using var process = new Process { StartInfo = new ProcessStartInfo(executable) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true } };
        foreach (var argument in arguments) { process.StartInfo.ArgumentList.Add(argument); }
        try
        {
            process.Start();
            var output = process.StandardOutput.ReadToEndAsync(cancellationToken);
            var error = process.StandardError.ReadToEndAsync(cancellationToken);
            await process.WaitForExitAsync(cancellationToken);
            return (process.ExitCode, await output, await error);
        }
        catch (OperationCanceledException)
        {
            try { if (!process.HasExited) { process.Kill(entireProcessTree: true); } }
            catch (InvalidOperationException) { }
            throw;
        }
    }
}
