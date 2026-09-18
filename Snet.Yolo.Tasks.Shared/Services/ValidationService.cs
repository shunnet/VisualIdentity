using SkiaSharp;
using Snet.Model.data;
using Snet.Yolo.Server;
using Snet.Yolo.Server.handler;
using Snet.Yolo.Server.@interface;
using Snet.Yolo.Server.models.data;
using Snet.Yolo.Server.models.@enum;
using Snet.Yolo.Tasks.Core.Training;
using YoloDotNet.Extensions;
using YoloDotNet.Models;

namespace Snet.Yolo.Tasks.Services;

/// <summary>后台视频识别完成后需要写回页面状态的结果。</summary>
public sealed record VideoRecognitionResult(string ResultJson, IReadOnlyList<ValidationDetection> Detections, string ResultUrl, double LastFrameRunTimeMilliseconds);

/// <summary>
/// 验证服务：模型管理(ManageOperate) + 本机推理(IdentityOperate)。
/// </summary>
public sealed class ValidationService
{
    private readonly ManageOperate _manage;
    private readonly CurrentUserContext _currentUser;
    private readonly MediaToolResolver _mediaTools;
    private readonly ICjkFontProvider _fonts;
    private readonly IExecutionProviderFactory _executionProviderFactory;
    private readonly ValidationFileLifetime _fileLifetime;
    private readonly ImagePreviewStore _previews;
    private readonly ILogger<ValidationService> _logger;

    /// <summary>创建验证服务并注入当前用户、媒体工具解析器、中文字体提供方和硬件执行提供程序工厂。</summary>
    public ValidationService(
        ManageOperate manage,
        CurrentUserContext currentUser,
        MediaToolResolver mediaTools,
        ICjkFontProvider fonts,
        IExecutionProviderFactory executionProviderFactory,
        ValidationFileLifetime fileLifetime,
        ImagePreviewStore previews,
        ILogger<ValidationService> logger)
    {
        _manage = manage;
        _currentUser = currentUser;
        _mediaTools = mediaTools;
        _fonts = fonts;
        _executionProviderFactory = executionProviderFactory;
        _fileLifetime = fileLifetime;
        _previews = previews;
        _logger = logger;
    }

    /// <summary>查询全部模型（清理文件已不存在的失效行）。</summary>
    public async Task<IReadOnlyList<OnnxData>> GetModelsAsync()
    {
        var owner = await _currentUser.GetRequiredUserNameAsync();
        var r = await _manage.QueryByOwnerAsync(owner);
        if (!r.GetDetails(out List<OnnxData>? list) || list is null) { return Array.Empty<OnnxData>(); }
        var valid = new List<OnnxData>();
        foreach (var m in list)
        {
            var p = Path.Combine(m.path ?? "", m.name ?? "");
            if (File.Exists(p)) { valid.Add(m); }
            else { await _manage.DeleteAsync(owner, m.index, true); }
        }
        return valid;
    }

    /// <summary>添加模型（保存到程序集目录 wwwroot/onnxs，不删）。</summary>
    public async Task<OperateResult> AddModelAsync(Stream onnx, string fileName, string describe, global::Snet.Yolo.Server.models.@enum.OnnxType type)
        => await AddModelForOwnerAsync(await _currentUser.GetRequiredUserNameAsync(), onnx, fileName, describe, type);

    internal async Task<OperateResult> AddModelForOwnerAsync(string owner, Stream onnx, string fileName, string describe, global::Snet.Yolo.Server.models.@enum.OnnxType type)
    {
        var savePath = Path.Combine(PublicHandler.DefaultPath, "onnxs", UserStoragePath.Segment(owner));
        if (!Directory.Exists(savePath)) { Directory.CreateDirectory(savePath); }
        var safeName = (Path.GetFileNameWithoutExtension(fileName ?? "model").Replace("..", "").Replace("/", "").Replace("\\", "")) + "_" + Guid.NewGuid().ToString("N").Substring(0, 8) + ".onnx";
        var filePath = Path.Combine(savePath, safeName);
        try
        {
            await using (var file = new FileStream(filePath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 128 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan))
            {
                await onnx.CopyToAsync(file);
            }
            NormalizeModelMetadata(filePath);
            var result = await _manage.AddAsync(owner, filePath, describe, type);
            if (!result.Status) { File.Delete(filePath); }
            return result;
        }
        catch
        {
            try { File.Delete(filePath); } catch { }
            throw;
        }
    }

    public async Task<OperateResult> DeleteModelAsync(int index) => await _manage.DeleteAsync(await _currentUser.GetRequiredUserNameAsync(), index, true);

    /// <summary>
    /// 规范化 ONNX 元数据：YoloDotNet 会从 description 解析模型型号，只认识到 YOLO11/v8/v5，
    /// 遇到 YOLO26 会在推理时抛 IndexOutOfRangeException。这里登记模型时就把型号名改写成等长的 YOLO11，
    /// 让“用 YOLO26 训练出来的模型”也能在验证页正常识别。
    /// </summary>
    internal void NormalizeModelMetadata(string modelPath)
    {
        try
        {
            var normalized = OnnxMetadata.TryNormalizeDescriptionFile(modelPath);
            if (normalized is not null)
            {
                _logger.LogInformation("已规范化 ONNX 元数据 description（YOLO26 → YOLO11）：{Description}", normalized);
            }
        }
        catch (Exception error)
        {
            // 元数据规范化失败不影响模型可用性，只记录
            _logger.LogWarning(error, "规范化 ONNX 元数据失败：{Path}", modelPath);
        }
    }

    public async Task DeleteValidationImageAsync(string? imageUrl)
    {
        var owner = await _currentUser.GetRequiredUserNameAsync();
        var ownerSegment = UserStoragePath.Segment(owner);
        var prefix = $"/uploads/{ownerSegment}/validation/";
        if (string.IsNullOrWhiteSpace(imageUrl) || !imageUrl.StartsWith(prefix, StringComparison.Ordinal)) { return; }
        var fileName = Uri.UnescapeDataString(imageUrl[prefix.Length..]);
        if (fileName != Path.GetFileName(fileName) || fileName is "." or "..") { return; }
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "wwwroot", "data", "uploads", ownerSegment, "validation"));
        var path = Path.GetFullPath(Path.Combine(root, fileName));
        if (path.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                File.Delete(path);
                _fileLifetime.Untrack(path);
                _previews.Delete(path);      // 原图删除时，它的预览图也一并删除
            }
            catch { }
        }
    }

    /// <summary>登记页面刚完成原子写入的验证文件，以便仅在当前进程结束时清理。</summary>
    public void TrackValidationFile(string path) => _fileLifetime.Track(path);

    /// <summary>更新模型。</summary>
    public async Task<OperateResult> UpdateModelAsync(int index, string describe, global::Snet.Yolo.Server.models.@enum.OnnxType? type) => await _manage.UpdateAsync(await _currentUser.GetRequiredUserNameAsync(), index, describe, type);

    /// <summary>返回当前用户的进程生命周期验证图片目录及 URL 前缀。</summary>
    public async ValueTask<(string Directory, string UrlPrefix)> GetValidationUploadLocationAsync()
    {
        var ownerSegment = UserStoragePath.Segment(await _currentUser.GetRequiredUserNameAsync());
        return (Path.Combine(AppContext.BaseDirectory, "wwwroot", "data", "uploads", ownerSegment, "validation"), $"/uploads/{ownerSegment}/validation/");
    }

    /// <summary>校验已暂存文件的真实媒体格式，拒绝伪造扩展名和异常尺寸。</summary>
    public async Task<(int Width, int Height)> ValidateUploadedFileAsync(string path, bool isVideo, CancellationToken cancellationToken = default)
    {
        if (!isVideo)
        {
            // 顺便把原图尺寸带回去：识别框坐标在这个空间里，前端要按它缩放到预览画布
            return UploadedFileValidator.ValidateImageAndGetDimensions(path);
        }
        _ = await ReadVideoMetadataAsync(path, cancellationToken);
        return (0, 0);   // 视频的叠加走 <video> 自身尺寸，不需要参考尺寸
    }

    /// <summary>
    /// 将视频帧流式落盘、逐帧推理并编码为带标注视频；任意时刻只在内存中保留一帧。
    /// </summary>
    public async Task<VideoRecognitionResult> ProcessVideoAsync(
        string owner,
        OnnxData model,
        ValidationImageState image,
        string paramJson,
        Action<VideoRecognitionStage, int, int, double> reportProgress,
        CancellationToken cancellationToken)
    {
        var videoPath = ResolveValidationFilePath(owner, image.Url);
        var metadata = await ReadVideoMetadataAsync(videoPath, cancellationToken);
        var estimatedFrames = Math.Max(1, (int)Math.Ceiling(metadata.DurationSeconds * metadata.FramesPerSecond));
        reportProgress(VideoRecognitionStage.Decoding, estimatedFrames, 0, metadata.FramesPerSecond);

        var temporaryDirectory = Path.Combine(Path.GetTempPath(), "snet-yolo-video", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temporaryDirectory);
        var sourcePattern = Path.Combine(temporaryDirectory, "source_%09d.jpg");
        var resultPattern = Path.Combine(temporaryDirectory, "result_%09d.jpg");
        var outputName = "result_" + Guid.NewGuid().ToString("N")[..12] + ".mp4";
        var outputDirectory = Path.GetDirectoryName(videoPath) ?? throw new InvalidOperationException("视频目录无效。");
        var temporaryOutput = Path.Combine(outputDirectory, Path.GetFileNameWithoutExtension(outputName) + ".processing.mp4");
        var finalOutput = Path.Combine(outputDirectory, outputName);

        try
        {
            await ExtractFramesToDirectoryAsync(videoPath, sourcePattern, cancellationToken);
            var frameFiles = Directory.GetFiles(temporaryDirectory, "source_*.jpg").OrderBy(path => path, StringComparer.Ordinal).ToArray();
            if (frameFiles.Length == 0) { throw new InvalidOperationException("FFmpeg 未能从视频中提取有效帧。"); }

            var dataType = model.onnxType ?? OnnxType.ObjectDetection;
            var effectiveParams = WithDefaults(paramJson, dataType);
            reportProgress(VideoRecognitionStage.LoadingModel, frameFiles.Length, 0, metadata.FramesPerSecond);
            await using var operate = CreateIdentityOperate(model, dataType);
            OperateResult? lastResult = null;
            // 整段视频的识别结果按标签累积，最后聚合成"平均置信度 + 总次数"
            var collected = new List<(string Name, double Confidence, string Position)>();

            for (var index = 0; index < frameFiles.Length; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var frameBytes = await File.ReadAllBytesAsync(frameFiles[index], cancellationToken);
                lastResult = await RunFrameAsync(operate, dataType, frameBytes, effectiveParams, cancellationToken);
                collected.AddRange(ParseRawDetections(lastResult));
                var resultPath = Path.Combine(temporaryDirectory, $"result_{index + 1:000000000}.jpg");
                SaveAnnotatedFrame(frameFiles[index], resultPath, lastResult, dataType, effectiveParams);
                reportProgress(VideoRecognitionStage.Recognizing, frameFiles.Length, index + 1, metadata.FramesPerSecond);
            }

            if (lastResult is null) { throw new InvalidOperationException("视频没有可识别的帧。"); }
            // 识别失败时不要把“无检测结果”当成正常结果：直接把原因抛给页面显示
            if (!lastResult.Status) { throw new InvalidOperationException("识别失败：" + (lastResult.Message ?? "未知原因")); }
            reportProgress(VideoRecognitionStage.Encoding, frameFiles.Length, frameFiles.Length, metadata.FramesPerSecond);
            await EncodeResultVideoAsync(resultPattern, videoPath, temporaryOutput, metadata.FramesPerSecond, cancellationToken);
            File.Move(temporaryOutput, finalOutput, true);
            _fileLifetime.Track(finalOutput);
            if (!string.IsNullOrWhiteSpace(image.ResultUrl)) { DeleteOwnedValidationFile(owner, image.ResultUrl); }

            var resultJson = System.Text.Json.JsonSerializer.Serialize(lastResult, new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
            return new VideoRecognitionResult(
                resultJson,
                AggregateDetections(collected),
                image.Url[..(image.Url.LastIndexOf('/') + 1)] + outputName,
                Convert.ToDouble(lastResult.RunTime, System.Globalization.CultureInfo.InvariantCulture));
        }
        catch
        {
            try { File.Delete(temporaryOutput); } catch { }
            try { File.Delete(finalOutput); } catch { }
            throw;
        }
        finally
        {
            try { Directory.Delete(temporaryDirectory, true); }
            catch { /* 临时帧由操作系统临时目录后续回收，不能覆盖原始识别异常。 */ }
        }
    }

    /// <summary>使用 FFmpeg 将视频解码为磁盘帧，避免将完整视频帧集放入托管内存。</summary>
    private async Task ExtractFramesToDirectoryAsync(string videoPath, string outputPattern, CancellationToken cancellationToken)
    {
        var process = CreateMediaProcess(_mediaTools.GetPaths().FFmpeg);
        AddArguments(process, "-hide_banner", "-loglevel", "error", "-i", videoPath, "-map", "0:v:0", "-fps_mode", "passthrough", "-q:v", "3", "-y", outputPattern);
        var result = await RunMediaProcessAsync(process, cancellationToken);
        if (result.ExitCode != 0 && result.Output.Contains("fps_mode", StringComparison.OrdinalIgnoreCase))
        {
            process = CreateMediaProcess(_mediaTools.GetPaths().FFmpeg);
            AddArguments(process, "-hide_banner", "-loglevel", "error", "-i", videoPath, "-map", "0:v:0", "-vsync", "0", "-q:v", "3", "-y", outputPattern);
            result = await RunMediaProcessAsync(process, cancellationToken);
        }
        if (result.ExitCode != 0) { throw new InvalidOperationException("FFmpeg 视频抽帧失败: " + result.Output); }
    }

    /// <summary>将已标注帧编码为浏览器可播放的 H.264 MP4，并尽可能复用原视频音轨。</summary>
    private async Task EncodeResultVideoAsync(string framePattern, string originalVideo, string outputPath, double framesPerSecond, CancellationToken cancellationToken)
    {
        var process = CreateMediaProcess(_mediaTools.GetPaths().FFmpeg);
        AddArguments(
            process,
            "-hide_banner", "-loglevel", "error", "-framerate", framesPerSecond.ToString("0.########", System.Globalization.CultureInfo.InvariantCulture),
            "-i", framePattern, "-i", originalVideo, "-map", "0:v:0", "-map", "1:a?", "-c:v", "libx264", "-pix_fmt", "yuv420p",
            "-c:a", "aac", "-shortest", "-movflags", "+faststart", "-y", outputPath);
        var result = await RunMediaProcessAsync(process, cancellationToken);
        if (result.ExitCode != 0) { throw new InvalidOperationException("FFmpeg 结果视频编码失败: " + result.Output); }
    }

    /// <summary>向媒体进程追加独立参数，避免命令行字符串拼接和转义问题。</summary>
    private static void AddArguments(System.Diagnostics.Process process, params string[] arguments)
    {
        foreach (var argument in arguments) { process.StartInfo.ArgumentList.Add(argument); }
    }

    /// <summary>
    /// 把单帧推理结果绘制到 JPEG；没有目标时直接复用原始帧。
    /// 注意：SkiaSharp 默认字体（Windows 的 Segoe UI、Linux 的 DejaVu Sans）没有中文字形，
    /// 不显式指定中文字体时标签会画成方框（tofu），所以这里统一传 <see cref="MediaFontResolver"/> 解析出的字体。
    /// </summary>
    private void SaveAnnotatedFrame(string sourcePath, string destinationPath, OperateResult result, OnnxType dataType, string paramJson)
    {
        var font = _fonts.Resolve();
        using var image = SKImage.FromEncodedData(sourcePath) ?? throw new InvalidOperationException("视频帧无法解码。");
        SKBitmap? bitmap = dataType switch
        {
            OnnxType.ObjectDetection when result.GetDetails(out List<ObjectDetectionResultData>? values) && values is { Count: > 0 }
                => image.Draw(values.ToObjectDetection(), new DetectionDrawingOptions { Font = font }),
            OnnxType.Segmentation when result.GetDetails(out List<SegmentationResultData>? values) && values is { Count: > 0 }
                => image.Draw(values.ToSegmentation(), new SegmentationDrawingOptions { Font = font }),
            OnnxType.Classification when result.GetDetails(out List<ClassificationResultData>? values) && values is { Count: > 0 }
                => image.Draw(values.ToClassification(), new ClassificationDrawingOptions { Font = font }),
            OnnxType.PoseEstimation when result.GetDetails(out List<PoseEstimationResultData>? values) && values is { Count: > 0 }
                => image.Draw(values.ToPoseEstimation(), new PoseDrawingOptions
                {
                    KeyPointMarkers = new PoseEstimationCustomKeyPointColorHandler().GetKeyPoints(),
                    PoseConfidence = FromJson<PoseEstimationData>(paramJson).Confidence,
                    BorderThickness = 3,
                    Font = font,
                }),
            OnnxType.ObbDetection when result.GetDetails(out List<ObbDetectionResultData>? values) && values is { Count: > 0 }
                => image.Draw(values.ToObbDetection(), new DetectionDrawingOptions { Font = font }),
            _ => null,
        };
        if (bitmap is null) { File.Copy(sourcePath, destinationPath, true); return; }
        using (bitmap)
        using (var encoded = bitmap.Encode(SKEncodedImageFormat.Jpeg, 90))
        using (var output = File.Create(destinationPath))
        {
            encoded.SaveTo(output);
        }
    }

    /// <summary>
    /// 把识别结果序列化为 JSON。
    /// 识别失败时 ResultData 里会带异常对象（例如 Exception.TargetSite 是 MethodBase），直接序列化会抛
    /// “Serialization and deserialization of 'System.Reflection.MethodBase' instances is not supported”，
    /// 反而把真正的失败原因吞掉；因此这里退化为只输出状态与消息。
    /// </summary>
    internal static string SerializeResult(OperateResult result, bool indented = false)
        => TrySerialize(result, indented)
           ?? TrySerialize(new Dictionary<string, object?>
           {
               ["Status"] = result.Status,
               ["Message"] = result.Message,
               ["RunTime"] = result.RunTime,
           }, indented)
           ?? "{}";

    /// <summary>序列化任意对象；遇到不支持的类型返回 null（由调用方退化处理），绝不抛出。</summary>
    internal static string? TrySerialize(object value, bool indented = false)
    {
        try { return System.Text.Json.JsonSerializer.Serialize(value, indented ? IndentedJsonOptions : JsonOptions); }
        catch (NotSupportedException) { return null; }
    }

    private static readonly System.Text.Json.JsonSerializerOptions JsonOptions = new();
    private static readonly System.Text.Json.JsonSerializerOptions IndentedJsonOptions = new() { WriteIndented = true };

    /// <summary>
    /// 从识别结果里提取"精简检测摘要"的原始数据（标签 / 0~1 置信度 / 坐标文本）。
    /// 照片取一次推理的结果；视频会在抽帧循环里逐帧累积后再聚合。
    /// </summary>
    internal static IReadOnlyList<(string Name, double Confidence, string Position)> ParseRawDetections(OperateResult result)
    {
        var detections = new List<(string, double, string)>();
        var json = SerializeResult(result);
        if (json == "{}") { return detections; }
        using var document = System.Text.Json.JsonDocument.Parse(json);
        if (!document.RootElement.TryGetProperty("ResultData", out var items) || items.ValueKind != System.Text.Json.JsonValueKind.Array) { return detections; }
        foreach (var item in items.EnumerateArray())
        {
            var name = "?";
            if (item.TryGetProperty("Label", out var label))
            {
                if (label.ValueKind == System.Text.Json.JsonValueKind.String) { name = label.GetString() ?? "?"; }
                else if (label.TryGetProperty("Name", out var labelName)) { name = labelName.GetString() ?? "?"; }
            }
            var confidence = item.TryGetProperty("Confidence", out var value) ? value.GetDouble() : 0d;
            var position = item.TryGetProperty("Position", out var positionValue) ? positionValue.GetString() ?? string.Empty : string.Empty;
            detections.Add((name, confidence, position));
        }
        return detections;
    }

    /// <summary>照片：每个目标一行（标签 / 置信度 / 坐标），保持与历史行为一致。</summary>
    internal static IReadOnlyList<ValidationDetection> ParseDetections(OperateResult result)
        => ParseRawDetections(result)
            .Select(item => new ValidationDetection(item.Name, FormatConfidence(item.Confidence), item.Position))
            .ToList();

    /// <summary>
    /// 视频：按标签聚合整段视频的识别结果——平均置信度 + 总识别次数；
    /// 坐标对视频没有意义（跨帧不一致），因此不再输出。次数多的在前。
    /// </summary>
    internal static IReadOnlyList<ValidationDetection> AggregateDetections(IEnumerable<(string Name, double Confidence, string Position)> detections)
    {
        var stats = new Dictionary<string, (int Count, double Sum)>(StringComparer.Ordinal);
        foreach (var item in detections)
        {
            stats.TryGetValue(item.Name, out var current);
            stats[item.Name] = (current.Count + 1, current.Sum + item.Confidence);
        }
        return stats
            .OrderByDescending(pair => pair.Value.Count)
            .ThenByDescending(pair => pair.Value.Sum / pair.Value.Count)
            .Select(pair => new ValidationDetection(pair.Key, FormatConfidence(pair.Value.Sum / pair.Value.Count), string.Empty, pair.Value.Count))
            .ToList();
    }

    /// <summary>置信度文本（0~1 → 百分比整数）。</summary>
    private static string FormatConfidence(double confidence)
        => Math.Round(confidence * 100) + "%";

    /// <summary>删除当前用户验证目录内的单个结果文件。</summary>
    private static void DeleteOwnedValidationFile(string owner, string url)
    {
        try { File.Delete(ResolveValidationFilePath(owner, url)); }
        catch (FileNotFoundException) { }
    }

    /// <summary>解析并验证当前用户验证目录中的视频文件路径。</summary>
    private static string ResolveValidationFilePath(string owner, string videoUrl)
    {
        var ownerSegment = UserStoragePath.Segment(owner);
        var prefix = $"/uploads/{ownerSegment}/validation/";
        if (string.IsNullOrWhiteSpace(videoUrl) || !videoUrl.StartsWith(prefix, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("视频地址不属于当前用户。");
        }
        var fileName = Uri.UnescapeDataString(videoUrl[prefix.Length..]);
        if (fileName != Path.GetFileName(fileName)) { throw new InvalidOperationException("视频文件名无效。"); }
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "wwwroot", "data", "uploads", ownerSegment, "validation"));
        var path = Path.GetFullPath(Path.Combine(root, fileName));
        if (!path.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) || !File.Exists(path))
        {
            throw new FileNotFoundException("找不到待识别的视频文件。", fileName);
        }
        return path;
    }

    /// <summary>通过 FFprobe 读取视频时长和平均帧率。</summary>
    private async Task<(double DurationSeconds, double FramesPerSecond)> ReadVideoMetadataAsync(string videoPath, CancellationToken cancellationToken)
    {
        var process = CreateMediaProcess(_mediaTools.GetPaths().FFprobe);
        process.StartInfo.ArgumentList.Add("-v");
        process.StartInfo.ArgumentList.Add("error");
        process.StartInfo.ArgumentList.Add("-show_entries");
        process.StartInfo.ArgumentList.Add("stream=avg_frame_rate,r_frame_rate:format=duration");
        process.StartInfo.ArgumentList.Add("-of");
        process.StartInfo.ArgumentList.Add("json");
        process.StartInfo.ArgumentList.Add(videoPath);
        var mediaResult = await RunMediaProcessAsync(process, cancellationToken, readStandardOutput: true);
        if (mediaResult.ExitCode != 0) { throw new InvalidOperationException("无法读取视频信息: " + mediaResult.Output); }
        try
        {
            using var document = System.Text.Json.JsonDocument.Parse(mediaResult.Output);
            var root = document.RootElement;
            var durationText = root.GetProperty("format").GetProperty("duration").GetString();
            var streams = root.GetProperty("streams");
            if (streams.ValueKind != System.Text.Json.JsonValueKind.Array || streams.GetArrayLength() == 0)
            {
                throw new InvalidOperationException("视频中未找到可识别的视频流。");
            }
            var stream = streams[0];
            var rateText = stream.GetProperty("avg_frame_rate").GetString();
            if (!double.TryParse(durationText, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var duration) || duration <= 0)
            {
                throw new InvalidOperationException("视频时长无效。");
            }
            var framesPerSecond = ParseFrameRate(rateText);
            if (framesPerSecond <= 0 && stream.TryGetProperty("r_frame_rate", out var realRate)) { framesPerSecond = ParseFrameRate(realRate.GetString()); }
            if (framesPerSecond <= 0) { throw new InvalidOperationException("视频帧率无效。"); }
            return (duration, framesPerSecond);
        }
        catch (Exception ex) when (ex is System.Text.Json.JsonException or KeyNotFoundException or FormatException or InvalidOperationException)
        {
            throw new InvalidOperationException("无法解析视频信息。", ex);
        }
    }

    /// <summary>解析 FFprobe 返回的整数或分数帧率。</summary>
    private static double ParseFrameRate(string? value)
    {
        var parts = (value ?? "0/1").Split('/');
        if (!double.TryParse(parts[0], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var numerator))
        {
            return 0;
        }
        var denominator = 1d;
        if (parts.Length > 1 && !double.TryParse(parts[1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out denominator))
        {
            return 0;
        }
        return denominator == 0 || !double.IsFinite(numerator) || !double.IsFinite(denominator) ? 0 : numerator / denominator;
    }

    /// <summary>创建不经过命令行拼接的媒体处理进程。</summary>
    private static System.Diagnostics.Process CreateMediaProcess(string executable)
        => new()
        {
            StartInfo = new System.Diagnostics.ProcessStartInfo
            {
                FileName = executable,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            },
        };

    /// <summary>运行媒体进程并完整读取输出，避免管道缓冲造成阻塞。</summary>
    private static async Task<(int ExitCode, string Output)> RunMediaProcessAsync(
        System.Diagnostics.Process process,
        CancellationToken cancellationToken,
        bool readStandardOutput = false)
    {
        try
        {
            process.Start();
            var outputTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
            var errorTask = process.StandardError.ReadToEndAsync(cancellationToken);
            await process.WaitForExitAsync(cancellationToken);
            var output = await outputTask;
            var error = await errorTask;
            return (process.ExitCode, readStandardOutput ? output : error);
        }
        catch (OperationCanceledException)
        {
            try
            {
                if (!process.HasExited) { process.Kill(entireProcessTree: true); }
            }
            catch (InvalidOperationException) { }
            throw;
        }
        catch (System.ComponentModel.Win32Exception ex)
        {
            throw new InvalidOperationException($"无法启动媒体工具 {process.StartInfo.FileName}: {ex.Message}", ex);
        }
        finally { process.Dispose(); }
    }

    /// <summary>本机推理（进程内 IdentityOperate，用法对齐 Api.Shared）。</summary>
    public async Task<OperateResult> RunLocalAsync(OnnxData model, byte[] image, string paramJson)
    {
        var dataType = model.onnxType ?? global::Snet.Yolo.Server.models.@enum.OnnxType.ObjectDetection;
        paramJson = WithDefaults(paramJson, dataType);
        using var operate = CreateIdentityOperate(model, dataType);
        return await operate.RunAsync(CreateInputData(dataType, image, paramJson));
    }

    /// <summary>已经在本次进程内核验过元数据的模型路径（避免每帧都重扫文件）。</summary>
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, byte> NormalizedModels = new(StringComparer.Ordinal);

    /// <summary>为指定模型创建一个可重复处理多帧的推理会话。</summary>
    private IdentityOperate CreateIdentityOperate(OnnxData model, global::Snet.Yolo.Server.models.@enum.OnnxType dataType)
    {
        var modelPath = Path.Combine(model.path ?? "", model.name ?? "");
        // 兼容早期导出的 YOLO26 模型：注册时没来得及规范化的话，这里补一次（同一路径只检查一次）
        if (NormalizedModels.TryAdd(modelPath, 0)) { NormalizeModelMetadata(modelPath); }
        return new IdentityOperate(new IdentityData
        {
            SN = $"{PublicHandler.DefaultSN}-local",
            Hardware = _executionProviderFactory.Create(modelPath),
            IdentifyType = dataType,
        });
    }

    /// <summary>根据模型类型构造当前帧的强类型识别参数。</summary>
    private static IData CreateInputData(global::Snet.Yolo.Server.models.@enum.OnnxType dataType, byte[] image, string paramJson)
    {
        IData data;
        switch (dataType)
        {
            case global::Snet.Yolo.Server.models.@enum.OnnxType.Classification:
                { var d = FromJson<ClassificationData>(paramJson); d.File = image; data = d; }
                break;
            case global::Snet.Yolo.Server.models.@enum.OnnxType.Segmentation:
                { var d = FromJson<SegmentationData>(paramJson); d.File = image; data = d; }
                break;
            case global::Snet.Yolo.Server.models.@enum.OnnxType.ObbDetection:
                { var d = FromJson<ObbDetectionData>(paramJson); d.File = image; data = d; }
                break;
            case global::Snet.Yolo.Server.models.@enum.OnnxType.PoseEstimation:
                { var d = FromJson<PoseEstimationData>(paramJson); d.File = image; data = d; }
                break;
            default:
                { var d = FromJson<ObjectDetectionData>(paramJson); d.File = image; data = d; }
                break;
        }
        return data;
    }

    /// <summary>按模型类型调用支持取消的强类型推理重载。</summary>
    private static Task<OperateResult> RunFrameAsync(
        IdentityOperate operate,
        OnnxType dataType,
        byte[] image,
        string paramJson,
        CancellationToken cancellationToken)
        => dataType switch
        {
            OnnxType.Classification => operate.RunAsync((ClassificationData)CreateInputData(dataType, image, paramJson), cancellationToken),
            OnnxType.Segmentation => operate.RunAsync((SegmentationData)CreateInputData(dataType, image, paramJson), cancellationToken),
            OnnxType.ObbDetection => operate.RunAsync((ObbDetectionData)CreateInputData(dataType, image, paramJson), cancellationToken),
            OnnxType.PoseEstimation => operate.RunAsync((PoseEstimationData)CreateInputData(dataType, image, paramJson), cancellationToken),
            _ => operate.RunAsync((ObjectDetectionData)CreateInputData(dataType, image, paramJson), cancellationToken),
        };

    /// <summary>识别参数兜底默认值（对齐 WPF 工具）：缺失键补齐，兼容历史键名(如 PixelConfedence)。</summary>
    private static string WithDefaults(string json, global::Snet.Yolo.Server.models.@enum.OnnxType type)
    {
        try
        {
            var node = System.Text.Json.Nodes.JsonNode.Parse(json) as System.Text.Json.Nodes.JsonObject ?? new();
            void Set(string k, double v) { if (node[k] is null) { node[k] = v; } }
            Set("Confidence", 0.25); Set("Iou", 0.45);
            if (type == global::Snet.Yolo.Server.models.@enum.OnnxType.Segmentation)
            {
                node["PixelConfidence"] ??= node["PixelConfedence"] ?? 0.65;
            }
            if (type == global::Snet.Yolo.Server.models.@enum.OnnxType.Classification) { Set("Classes", 1); }
            return node.ToJsonString();
        }
        catch { return json; }
    }

    /// <summary>反序列化识别参数，并兼容旧版页面生成的字符串数值。</summary>
    internal static T FromJson<T>(string json) where T : new()
    {
        try
        {
            return System.Text.Json.JsonSerializer.Deserialize<T>(json, new System.Text.Json.JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
                NumberHandling = System.Text.Json.Serialization.JsonNumberHandling.AllowReadingFromString,
            }) ?? throw new InvalidOperationException("识别参数不能为空。");
        }
        catch (System.Text.Json.JsonException ex)
        {
            throw new InvalidOperationException("识别参数格式无效。", ex);
        }
    }
}
