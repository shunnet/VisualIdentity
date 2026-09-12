using Snet.Model.data;
using Snet.Yolo.Server;
using Snet.Yolo.Server.handler;
using Snet.Yolo.Server.@interface;
using Snet.Yolo.Server.models.data;

namespace Snet.Yolo.Tasks.Services;

/// <summary>视频抽帧结果，包含视频时长、帧率和按时间顺序排列的 JPEG 帧。</summary>
public sealed record VideoFrameExtraction(double DurationSeconds, double FramesPerSecond, IReadOnlyList<byte[]> Frames);

/// <summary>
/// 验证服务：模型管理(ManageOperate) + 本机推理(IdentityOperate)。
/// </summary>
public sealed class ValidationService
{
    private readonly ManageOperate _manage;
    private readonly CurrentUserContext _currentUser;
    private readonly MediaToolResolver _mediaTools;
    private readonly IExecutionProviderFactory _executionProviderFactory;

    /// <summary>创建验证服务并注入当前用户、媒体工具解析器和硬件执行提供程序工厂。</summary>
    public ValidationService(
        ManageOperate manage,
        CurrentUserContext currentUser,
        MediaToolResolver mediaTools,
        IExecutionProviderFactory executionProviderFactory)
    {
        _manage = manage;
        _currentUser = currentUser;
        _mediaTools = mediaTools;
        _executionProviderFactory = executionProviderFactory;
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
            try { File.Delete(path); } catch { }
        }
    }

    /// <summary>更新模型。</summary>
    public async Task<OperateResult> UpdateModelAsync(int index, string describe, global::Snet.Yolo.Server.models.@enum.OnnxType? type) => await _manage.UpdateAsync(await _currentUser.GetRequiredUserNameAsync(), index, describe, type);

    /// <summary>返回当前用户的进程生命周期验证图片目录及 URL 前缀。</summary>
    public async ValueTask<(string Directory, string UrlPrefix)> GetValidationUploadLocationAsync()
    {
        var ownerSegment = UserStoragePath.Segment(await _currentUser.GetRequiredUserNameAsync());
        return (Path.Combine(AppContext.BaseDirectory, "wwwroot", "data", "uploads", ownerSegment, "validation"), $"/uploads/{ownerSegment}/validation/");
    }

    /// <summary>使用服务器 FFmpeg 解码当前用户视频的全部帧。</summary>
    public async Task<VideoFrameExtraction> ExtractVideoFramesAsync(
        string owner,
        string videoUrl,
        CancellationToken cancellationToken)
    {
        var videoPath = ResolveValidationFilePath(owner, videoUrl);
        var metadata = await ReadVideoMetadataAsync(videoPath, cancellationToken);
        var temporaryRoot = Path.Combine(Path.GetTempPath(), "snet-yolo-video-frames");
        var outputDirectory = Path.Combine(temporaryRoot, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(outputDirectory);
        try
        {
            var outputPattern = Path.Combine(outputDirectory, "frame_%04d.jpg");
            var process = CreateMediaProcess(_mediaTools.GetPaths().FFmpeg);
            process.StartInfo.ArgumentList.Add("-hide_banner");
            process.StartInfo.ArgumentList.Add("-loglevel");
            process.StartInfo.ArgumentList.Add("error");
            process.StartInfo.ArgumentList.Add("-i");
            process.StartInfo.ArgumentList.Add(videoPath);
            process.StartInfo.ArgumentList.Add("-map");
            process.StartInfo.ArgumentList.Add("0:v:0");
            process.StartInfo.ArgumentList.Add("-fps_mode");
            process.StartInfo.ArgumentList.Add("passthrough");
            process.StartInfo.ArgumentList.Add("-q:v");
            process.StartInfo.ArgumentList.Add("3");
            process.StartInfo.ArgumentList.Add("-y");
            process.StartInfo.ArgumentList.Add(outputPattern);
            var mediaResult = await RunMediaProcessAsync(process, cancellationToken);
            if (mediaResult.ExitCode != 0) { throw new InvalidOperationException("FFmpeg 视频抽帧失败: " + mediaResult.Output); }

            var files = Directory.GetFiles(outputDirectory, "frame_*.jpg").OrderBy(path => path, StringComparer.Ordinal).ToArray();
            if (files.Length == 0) { throw new InvalidOperationException("FFmpeg 未能从视频中提取有效帧。"); }
            var frames = new List<byte[]>(files.Length);
            foreach (var file in files) { frames.Add(await File.ReadAllBytesAsync(file, cancellationToken)); }
            return new VideoFrameExtraction(metadata.DurationSeconds, metadata.FramesPerSecond, frames);
        }
        finally
        {
            try { if (Directory.Exists(outputDirectory)) { Directory.Delete(outputDirectory, true); } }
            catch { /* 临时帧清理失败不覆盖识别结果，系统临时目录后续仍可回收。 */ }
        }
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
        process.StartInfo.ArgumentList.Add("stream=avg_frame_rate:format=duration");
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
            var rateText = root.GetProperty("streams")[0].GetProperty("avg_frame_rate").GetString();
            if (!double.TryParse(durationText, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var duration) || duration <= 0)
            {
                throw new InvalidOperationException("视频时长无效。");
            }
            var rateParts = (rateText ?? "0/1").Split('/');
            var numerator = double.Parse(rateParts[0], System.Globalization.CultureInfo.InvariantCulture);
            var denominator = rateParts.Length > 1 ? double.Parse(rateParts[1], System.Globalization.CultureInfo.InvariantCulture) : 1;
            var framesPerSecond = denominator == 0 ? 0 : numerator / denominator;
            return (duration, framesPerSecond);
        }
        catch (Exception ex) when (ex is System.Text.Json.JsonException or KeyNotFoundException or FormatException or InvalidOperationException)
        {
            throw new InvalidOperationException("无法解析视频信息。", ex);
        }
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

    /// <summary>在同一个模型推理会话中顺序识别全部视频帧，避免每帧重复加载 ONNX 模型。</summary>
    public async Task<IReadOnlyList<OperateResult>> RunVideoFramesAsync(
        OnnxData model,
        IReadOnlyList<byte[]> frames,
        string paramJson,
        Func<int, OperateResult, Task> frameCompleted,
        CancellationToken cancellationToken)
    {
        var dataType = model.onnxType ?? global::Snet.Yolo.Server.models.@enum.OnnxType.ObjectDetection;
        var effectiveParams = WithDefaults(paramJson, dataType);
        using var operate = CreateIdentityOperate(model, dataType);
        var results = new List<OperateResult>(frames.Count);
        for (var index = 0; index < frames.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var result = await operate.RunAsync(CreateInputData(dataType, frames[index], effectiveParams));
            results.Add(result);
            await frameCompleted(index, result);
        }
        return results;
    }

    /// <summary>为指定模型创建一个可重复处理多帧的推理会话。</summary>
    private IdentityOperate CreateIdentityOperate(OnnxData model, global::Snet.Yolo.Server.models.@enum.OnnxType dataType)
        => new(new IdentityData
        {
            SN = $"{PublicHandler.DefaultSN}-local",
            Hardware = _executionProviderFactory.Create(Path.Combine(model.path ?? "", model.name ?? "")),
            IdentifyType = dataType,
        });

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
