namespace Snet.Yolo.Tasks.Services;

using Snet.Yolo.Server.anomalib;
using Snet.Yolo.Tasks.Core.Anomalib;
using Snet.Yolo.Tasks.Core.History;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using System.Text.Json;

/// <summary>
/// Anomalib 第一阶段训练编排：正常图稳定划分、独立 Python 流水线、ONNX 导出、
/// 训练模型与 ONNX 一致性门禁以及门禁后的模型注册。
/// </summary>
public sealed class AnomalibTrainingService
{
    /// <summary>JSON 序列化设置，输出驼峰字段并便于现场排查。</summary>
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
    };

    /// <summary>进程运行器。</summary>
    private readonly IAnomalibProcessRunner _runner;

    /// <summary>通过一致性门禁后的模型注册器。</summary>
    private readonly IAnomalibModelRegistrar _registrar;

    /// <summary>训练记录存储；为空时不保存训练档案。</summary>
    private readonly TrainingHistoryStore? _history;

    /// <summary>日志记录器；为空时不输出日志。</summary>
    private readonly Microsoft.Extensions.Logging.ILogger<AnomalibTrainingService>? _logger;

    /// <summary>识别 tqdm 进度条行（如 <c>96%|█████▋| 104167/107999</c>）。</summary>
    private static readonly Regex ProgressBarLine = new(@"\d{1,3}%\|", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <summary>创建独立 Anomalib 训练服务。</summary>
    /// <param name="runner">不经过 shell 的进程运行器。</param>
    /// <param name="registrar">门禁后的模型注册器。</param>
    /// <param name="history">训练记录存储；省略时不保存训练档案。</param>
    /// <param name="logger">日志记录器；省略时不输出日志。</param>
    public AnomalibTrainingService(IAnomalibProcessRunner runner, IAnomalibModelRegistrar registrar, TrainingHistoryStore? history = null,
        Microsoft.Extensions.Logging.ILogger<AnomalibTrainingService>? logger = null)
    {
        _runner = runner ?? throw new ArgumentNullException(nameof(runner));
        _registrar = registrar ?? throw new ArgumentNullException(nameof(registrar));
        _history = history;
        _logger = logger;
    }

    /// <summary>执行一次完整训练；任何门禁失败都不会调用注册器。</summary>
    /// <param name="request">已经完成用户授权和图片格式校验的训练请求。</param>
    /// <param name="onStatus">可选状态快照回调。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>训练结果与一致性证据。</returns>
    public async Task<AnomalibTrainingResult> TrainAsync(
        AnomalibTrainingRequest request,
        Action<AnomalibTrainingStatus>? onStatus,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ValidateRequest(request);
        request.Options.Validate();
        var status = new AnomalibTrainingStatus
        {
            Owner = request.Owner,
            ProjectId = request.ProjectId,
            Model = request.Options.Model,
            Device = request.Options.Device,
            ImageSize = request.Options.ImageSize,
            MaxEpochs = request.Options.MaxEpochs,
            EarlyStoppingEnabled = request.Options.EarlyStoppingEnabled,
            EarlyStoppingPatience = request.Options.EarlyStoppingPatience,
            EarlyStoppingMinDelta = request.Options.EarlyStoppingMinDelta,
            Phase = AnomalibTrainingPhase.PreparingDataset,
            Message = "正在生成正常图片训练集与校准集……",
            StartedAt = DateTime.UtcNow,
        };
        Publish(status, onStatus);
        string? runDirectory = null;
        var registered = false;
        var retainExport = false;
        AnomalibParityResult? recordedParity = null;
        AnomalibRunMetricsFile? recordedMetrics = null;

        try
        {
            var sources = await HashImagesAsync(request.Images, cancellationToken);
            var split = AnomalibDatasetPlanner.Create(sources, request.Options);
            status.TrainingImageCount = split.TrainingImages.Count;
            status.CalibrationImageCount = split.CalibrationImages.Count;
            status.DatasetSha256 = split.DatasetSha256;
            Publish(status, onStatus);

            runDirectory = Path.Combine(Path.GetFullPath(request.WorkingDirectory), "anomalib-run-" + Guid.NewGuid().ToString("N"));
            var datasetDirectory = Path.Combine(runDirectory, "dataset");
            var artifactDirectory = Path.Combine(runDirectory, "artifacts");
            var runtimeDirectory = Path.Combine(AppContext.BaseDirectory, "train", "anomalib", "scripts");
            Directory.CreateDirectory(runDirectory);
            await MaterializeDatasetAsync(datasetDirectory, split, cancellationToken);
            var scriptPath = AnomalibPythonPipeline.Materialize(runtimeDirectory);
            var configurationPath = Path.Combine(runDirectory, "pipeline.config.json");
            var resultPath = Path.Combine(runDirectory, "result.json");
            await WriteConfigurationAsync(configurationPath, datasetDirectory, artifactDirectory, split, request.Options, cancellationToken);

            status.Phase = AnomalibTrainingPhase.Training;
            status.Message = "正在训练 Anomalib 模型……";
            Publish(status, onStatus);
            var command = AnomalibCommandBuilder.BuildPipeline(request.PythonExecutable, scriptPath, configurationPath, resultPath);
            var process = await _runner.RunAsync(
                command,
                runDirectory,
                line => HandlePipelineOutput(line, status, onStatus),
                cancellationToken);
            var reachedParity = status.Phase == AnomalibTrainingPhase.ValidatingParity;
            recordedMetrics = AnomalibRunMetricsFile.TryRead(Path.Combine(artifactDirectory, "metrics.json"));
            var parity = await ReadParityAsync(resultPath, process, cancellationToken);
            recordedParity = parity;
            if (!parity.CanRegister && !(process.ExitCode == 0 && reachedParity && parity.ExportSucceeded))
            {
                status.Phase = AnomalibTrainingPhase.Failed;
                status.LastError = parity.FailureReason;
                status.Message = reachedParity ? "模型一致性验证失败，未注册模型。" : "Anomalib 训练流水线失败，未注册模型。";
                Publish(status, onStatus);
                var failureHint = !reachedParity && parity.FailureReason.Contains("certificate verify failed", StringComparison.OrdinalIgnoreCase)
                    ? " Python 下载 EfficientAD 预训练权重时证书校验失败；请让运行训练的系统信任代理/网关的 CA，或配置 Training:CaBundle 为该系统可读取的 PEM 证书包路径后重启服务。不要关闭证书校验。"
                    : string.Empty;
                return new AnomalibTrainingResult
                {
                    Succeeded = false,
                    Message = status.Message + " " + parity.FailureReason + failureHint,
                    Parity = parity,
                };
            }

            var artifact = CreateExportedArtifact(request, artifactDirectory, split.DatasetSha256, parity);
            status.Calibration = parity;
            status.ExportRunId = Path.GetFileName(runDirectory);
            if (!parity.CanRegister)
            {
                retainExport = true;
                status.Phase = AnomalibTrainingPhase.CalibrationFailed;
                status.LastError = parity.FailureReason;
                status.Message = "模型已导出，校准未通过；可下载模型包后自行决定是否手动导入验证页。";
                Publish(status, onStatus);
                return new AnomalibTrainingResult { Succeeded = false, Message = status.Message, Parity = parity, Artifact = artifact };
            }
            await _registrar.RegisterAsync(artifact, cancellationToken);
            registered = true;
            status.Phase = AnomalibTrainingPhase.Complete;
            status.Message = "Anomalib 模型已通过一致性验证并完成注册。";
            Publish(status, onStatus);
            return new AnomalibTrainingResult
            {
                Succeeded = true,
                Message = status.Message,
                Parity = parity,
                Artifact = artifact,
            };
        }
        catch (OperationCanceledException)
        {
            status.ExportRunId = null;
            status.Phase = AnomalibTrainingPhase.Cancelled;
            status.Message = "Anomalib 训练已取消。";
            Publish(status, onStatus);
            throw;
        }
        catch (Exception error)
        {
            status.ExportRunId = null;
            status.Phase = AnomalibTrainingPhase.Failed;
            status.LastError = error.Message;
            status.Message = "Anomalib 训练失败。";
            Publish(status, onStatus);
            return new AnomalibTrainingResult
            {
                Succeeded = false,
                Message = status.Message + " " + error.Message,
                Parity = AnomalibParityResult.NotRun(error.Message),
            };
        }
        finally
        {
            CleanupRunDirectory(runDirectory, retainModel: registered || retainExport);
            await SaveRunRecordAsync(request, runDirectory, status, recordedParity, recordedMetrics);
        }
    }

    /// <summary>
    /// 清理本次运行的临时数据：数据集副本与缩放后的训练副本只服务于这次训练，可能多达数十 GB。
    /// 保留模型（已注册或校准未通过但完整导出）时只删除它们，其余产物留给下载和留档；没有模型时整个目录删除。
    /// </summary>
    /// <param name="runDirectory">本次运行目录；尚未创建时为空。</param>
    /// <param name="retainModel">是否保留模型、配置和报告。</param>
    private static void CleanupRunDirectory(string? runDirectory, bool retainModel)
    {
        if (runDirectory is null || !Directory.Exists(runDirectory)) { return; }
        var targets = retainModel
            ? new[] { Path.Combine(runDirectory, "dataset"), Path.Combine(runDirectory, "working") }
            : [runDirectory];
        foreach (var target in targets)
        {
            try { if (Directory.Exists(target)) { Directory.Delete(target, recursive: true); } }
            catch (IOException) { /* 文件占用时保留原始训练结果。 */ }
            catch (UnauthorizedAccessException) { /* 清理失败不得掩盖训练结果。 */ }
        }
    }

    /// <summary>保存本次训练的记录；训练档案只是辅助信息，写入失败只记日志，不影响训练结果。</summary>
    private async Task SaveRunRecordAsync(AnomalibTrainingRequest request, string? runDirectory, AnomalibTrainingStatus status,
        AnomalibParityResult? parity, AnomalibRunMetricsFile? metrics)
    {
        if (_history is null) { return; }
        try
        {
            // 运行目录创建之前就失败或被取消（去重后图片不足、读取出错等）同样要留档，只是没有模型与指标。
            TrainingRunRecord record;
            if (runDirectory is null)
            {
                record = AnomalibRunRecordBuilder.BuildEarlyStop(request.Owner, request.ProjectId, request.Options, status, status.StartedAt ?? DateTime.UtcNow, DateTime.UtcNow);
            }
            else
            {
                var onnx = Path.Combine(runDirectory, "artifacts", "model.onnx");
                var modelBytes = File.Exists(onnx) ? new FileInfo(onnx).Length : 0;
                record = AnomalibRunRecordBuilder.Build(request, Path.GetFileName(runDirectory), status, parity, metrics, DateTime.UtcNow, modelBytes);
            }
            await _history.SaveAsync(request.Owner, record, CancellationToken.None);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException)
        {
            _logger?.LogWarning(error, "无法保存工程 {ProjectId} 的训练记录", request.ProjectId);
        }
    }

    /// <summary>校验训练请求的必填字段与图片文件边界。</summary>
    private static void ValidateRequest(AnomalibTrainingRequest request)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Owner);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.ProjectId);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.WorkingDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.PythonExecutable);
        ArgumentNullException.ThrowIfNull(request.Images);
        ArgumentNullException.ThrowIfNull(request.Options);
        if (request.Images.Count == 0) { throw new InvalidOperationException("没有可用于训练的正常图片。"); }
        foreach (var path in request.Images)
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) { throw new FileNotFoundException("正常图片不存在。", path); }
            _ = UploadedFileValidator.GetExtension(path, allowVideo: false);
        }
    }

    /// <summary>异步计算正常图片内容摘要，取消后立即停止读取。</summary>
    private static async Task<IReadOnlyList<AnomalibImageSource>> HashImagesAsync(
        IReadOnlyList<string> imagePaths,
        CancellationToken cancellationToken)
    {
        var result = new List<AnomalibImageSource>(imagePaths.Count);
        foreach (var path in imagePaths)
        {
            await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1024 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
            var hash = await SHA256.HashDataAsync(stream, cancellationToken);
            result.Add(new AnomalibImageSource(Path.GetFullPath(path), Convert.ToHexStringLower(hash)));
        }
        return result;
    }

    /// <summary>把稳定划分复制到 Folder 数据模块要求的 train/good 与 calibration/good 目录。</summary>
    private static async Task MaterializeDatasetAsync(
        string datasetDirectory,
        AnomalibDatasetSplit split,
        CancellationToken cancellationToken)
    {
        var trainDirectory = Path.Combine(datasetDirectory, "train", "good");
        var calibrationDirectory = Path.Combine(datasetDirectory, "calibration", "good");
        Directory.CreateDirectory(trainDirectory);
        Directory.CreateDirectory(calibrationDirectory);
        await CopyImagesAsync(split.TrainingImages, trainDirectory, cancellationToken);
        await CopyImagesAsync(split.CalibrationImages, calibrationDirectory, cancellationToken);
    }

    /// <summary>以内容摘要命名复制图片，避免同名文件覆盖并保证布局可复现。</summary>
    private static async Task CopyImagesAsync(
        IReadOnlyList<AnomalibImageSource> images,
        string destinationDirectory,
        CancellationToken cancellationToken)
    {
        foreach (var image in images)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var extension = Path.GetExtension(image.SourcePath).ToLowerInvariant();
            var destination = Path.Combine(destinationDirectory, image.ContentSha256 + extension);
            await using var source = new FileStream(image.SourcePath, FileMode.Open, FileAccess.Read, FileShare.Read, 1024 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
            await using var target = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None, 1024 * 1024, FileOptions.Asynchronous | FileOptions.WriteThrough);
            await source.CopyToAsync(target, cancellationToken);
        }
    }

    /// <summary>写入只包含数据与受限选项的 Python 配置，不包含可执行代码。</summary>
    private static async Task WriteConfigurationAsync(
        string configurationPath,
        string datasetDirectory,
        string artifactDirectory,
        AnomalibDatasetSplit split,
        AnomalibTrainingOptions options,
        CancellationToken cancellationToken)
    {
        var descriptor = AnomalibModelCatalog.Get(options.Model);
        var configuration = new
        {
            model = descriptor.PythonName,
            imageSize = options.ImageSize,
            maxEpochs = options.MaxEpochs,
            earlyStoppingEnabled = options.EarlyStoppingEnabled,
            earlyStoppingPatience = options.EarlyStoppingPatience,
            earlyStoppingMinDelta = options.EarlyStoppingMinDelta,
            calibrationFileNames = split.CalibrationImages.ToDictionary(
                image => image.ContentSha256 + Path.GetExtension(image.SourcePath).ToLowerInvariant(),
                image => Path.GetFileName(image.SourcePath), StringComparer.Ordinal),
            device = options.Device,
            workerCount = options.WorkerCount,
            randomSeed = options.RandomSeed,
            datasetRoot = Path.GetFullPath(datasetDirectory),
            artifactRoot = Path.GetFullPath(artifactDirectory),
            imagenetteCacheRoot = Path.Combine(AppContext.BaseDirectory, "train", "anomalib", ".cache", "imagenette"),
            datasetSha256 = split.DatasetSha256,
            normalImageCount = split.TrainingImages.Count,
            calibrationImageCount = split.CalibrationImages.Count,
            maximumNormalFalsePositiveRate = AnomalibTrainingOptions.MaximumNormalFalsePositiveRate,
        };
        var json = JsonSerializer.Serialize(configuration, JsonOptions);
        await File.WriteAllTextAsync(configurationPath, json, cancellationToken);
    }

    /// <summary>读取并二次校验 Python 门禁结果；进程失败但有结构化结果时优先返回精确原因。</summary>
    private static async Task<AnomalibParityResult> ReadParityAsync(
        string resultPath,
        AnomalibProcessResult process,
        CancellationToken cancellationToken)
    {
        if (!File.Exists(resultPath))
        {
            return AnomalibParityResult.NotRun(process.ExitCode == 0
                ? "Python 未写出一致性结果。"
                : $"Python 流水线退出码 {process.ExitCode}：{process.OutputTail}");
        }
        var json = await File.ReadAllTextAsync(resultPath, cancellationToken);
        try
        {
            var parity = AnomalibParityResult.Parse(json);
            return process.ExitCode != 0 && (parity.CanRegister || parity.ExportSucceeded)
                ? AnomalibParityResult.NotRun($"Python 流水线退出码 {process.ExitCode}：{process.OutputTail}")
                : parity;
        }
        catch (InvalidDataException error) { return AnomalibParityResult.NotRun(error.Message); }
    }

    /// <summary>确认全部导出文件存在且不为空，再创建待注册或手动下载的产物对象。</summary>
    private static AnomalibTrainingArtifact CreateExportedArtifact(
        AnomalibTrainingRequest request,
        string artifactDirectory,
        string datasetSha256,
        AnomalibParityResult parity)
    {
        var checkpoint = RequireArtifact(artifactDirectory, "model.ckpt");
        var onnx = RequireArtifact(artifactDirectory, "model.onnx");
        var manifest = RequireArtifact(artifactDirectory, "model.manifest.json");
        return new AnomalibTrainingArtifact
        {
            Owner = request.Owner,
            ProjectId = request.ProjectId,
            Model = request.Options.Model,
            CheckpointPath = checkpoint,
            OnnxPath = onnx,
            ManifestPath = manifest,
            DatasetSha256 = datasetSha256,
            Parity = parity,
        };
    }

    /// <summary>要求产物位于预期目录且包含内容。</summary>
    private static string RequireArtifact(string artifactDirectory, string fileName)
    {
        var path = Path.Combine(Path.GetFullPath(artifactDirectory), fileName);
        var info = new FileInfo(path);
        if (!info.Exists || info.Length == 0) { throw new InvalidDataException("训练产物缺失或为空：" + fileName); }
        return info.FullName;
    }

    /// <summary>解析 Python 阶段标记并发布状态，其余输出留给上层日志管道处理。</summary>
    private static void HandlePipelineOutput(
        string line,
        AnomalibTrainingStatus status,
        Action<AnomalibTrainingStatus>? onStatus)
    {
        lock (status)
        {
            // 结构化进度行只用来刷新实时指标，不进入用户可见的日志。
            if (AnomalibProgressParser.IsStructured(line))
            {
                if (AnomalibProgressParser.TryApply(line, status.Live)) { Publish(status, onStatus); }
                return;
            }
            var lines = status.LogTail.ToList();
            // 进度条每次刷新都会产生新的一行；只保留最新一行，避免有效日志被挤出尾部。
            if (lines.Count > 0 && ProgressBarLine.IsMatch(line) && ProgressBarLine.IsMatch(lines[^1])) { lines[^1] = line; }
            else { lines.Add(line); }
            if (lines.Count > 120) { lines.RemoveRange(0, lines.Count - 120); }
            status.LogTail = lines;
            if (line.Contains("VISUALIDENTITY_PHASE:exporting", StringComparison.Ordinal))
            {
                status.Phase = AnomalibTrainingPhase.Exporting;
                status.Message = "正在导出 Anomalib ONNX 模型……";
            }
            else if (line.Contains("VISUALIDENTITY_PHASE:parity", StringComparison.Ordinal))
            {
                status.Phase = AnomalibTrainingPhase.ValidatingParity;
                status.Message = "正在比较训练模型与 ONNX 输出一致性……";
            }
            Publish(status, onStatus);
        }
    }

    /// <summary>更新时间戳并发布独立状态快照，避免回调修改服务内部状态。</summary>
    private static void Publish(AnomalibTrainingStatus status, Action<AnomalibTrainingStatus>? onStatus)
    {
        status.UpdatedAt = DateTime.UtcNow;
        onStatus?.Invoke(status.Clone());
    }
}
