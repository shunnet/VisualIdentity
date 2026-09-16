using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Configuration;
using Snet.Yolo.Server.models.@enum;
using Snet.Yolo.Tasks.Core.Config;
using Snet.Yolo.Tasks.Core.Editing;
using Snet.Yolo.Tasks.Core.Models;
using Snet.Yolo.Tasks.Core.Serialization.Export;
using Snet.Yolo.Tasks.Core.Training;
using Snet.Yolo.Tasks.Core.Workspace;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;

namespace Snet.Yolo.Tasks.Services;

/// <summary>训练编排：导出数据集 -> 检测/搭建环境 -> 运行训练 -> 实时进度/日志（SignalR）。</summary>
public sealed class TrainingService : IAsyncDisposable
{
    private static readonly long StatusBroadcastIntervalMs = 250;
    private readonly IHubContext<TrainingHub> _hub;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<TrainingService> _logger;
    /// <summary>应用配置的 pip 代理（Training:Proxy）；未配置或为空则为 null。</summary>
    private readonly string? _configuredProxy;
    private readonly ConcurrentDictionary<string, TrainingStatus> _statuses = new();
    private readonly ConcurrentDictionary<string, Process> _processes = new();
    private readonly ConcurrentDictionary<string, CancellationTokenSource> _runCancellations = new();
    private readonly ConcurrentDictionary<string, long> _lastStatusBroadcastAt = new();
    private readonly object _statusFileLock = new();
    private readonly CancellationTokenSource _lifetimeCancellation = new();
    private Task? _pipelineTask;
    private int _disposed;
    private int _running;

    /// <summary>共享训练环境 venv 目录（程序集目录下，跨工程共用，一个环境支持多个训练）。</summary>
    private static string VenvRoot => Path.Combine(AppContext.BaseDirectory, "train", ".env");

    /// <summary>训练状态持久化目录（程序集目录下，重启后恢复各项目的训练信息）。</summary>
    private static string StatusDir => Path.Combine(AppContext.BaseDirectory, "train", "statuses");

    public TrainingService(IHubContext<TrainingHub> hub, IServiceScopeFactory scopeFactory, ILogger<TrainingService> logger, IConfiguration? configuration = null)
    {
        _hub = hub;
        _scopeFactory = scopeFactory;
        _logger = logger;
        // 配置节缺失时 ReadProxy 返回 null，不影响默认行为
        _configuredProxy = ReadProxy(configuration);
        LoadStatuses();
    }

    /// <summary>读取 Training:Proxy；节缺失、为空或全空白都返回 null。</summary>
    public static string? ReadProxy(IConfiguration? configuration)
    {
        try
        {
            var value = configuration?["Training:Proxy"];
            return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        }
        catch (Exception)
        {
            // 配置提供程序异常不应阻断训练
            return null;
        }
    }

    private void LoadStatuses()
    {
        try
        {
            var dir = StatusDir;
            if (!Directory.Exists(dir)) { return; }
            foreach (var f in Directory.EnumerateFiles(dir, "*.json", SearchOption.AllDirectories))
            {
                try
                {
                    var st = System.Text.Json.JsonSerializer.Deserialize<TrainingStatus>(File.ReadAllText(f));
                    if (st is null || string.IsNullOrEmpty(st.ProjectId) || string.IsNullOrEmpty(st.Owner)) { continue; }
                    // 重启后：上次运行中的训练视为已中断；完成的训练校验 best.pt 仍存在
                    if (st.IsActive) { st.Phase = TrainingPhase.Cancelled; st.Message = "上次训练已中断（应用重启）"; }
                    if (st.Phase == TrainingPhase.Complete && (string.IsNullOrEmpty(st.BestModelPath) || !File.Exists(st.BestModelPath)))
                    { st.Phase = TrainingPhase.Idle; st.BestModelPath = ""; st.Message = ""; }
                    st.LogTail.Clear();
                    _statuses[Key(st.Owner, st.ProjectId)] = st;
                }
                catch (Exception error) { _logger.LogWarning(error, "无法读取训练状态文件 {StatusFile}", f); }
            }
        }
        catch (Exception error) { _logger.LogWarning(error, "无法加载训练状态目录 {StatusDirectory}", StatusDir); }
    }

    private void SaveStatus(TrainingStatus s)
    {
        try
        {
            TrainingStatus clone;
            lock (s) { clone = s.Clone(); }
            clone.LogTail.Clear();
            var json = System.Text.Json.JsonSerializer.Serialize(clone);
            var ownerDirectory = Path.Combine(StatusDir, UserStoragePath.Segment(s.Owner));
            var path = Path.Combine(ownerDirectory, SanitizeFileName(s.ProjectId) + ".json");
            var temporaryPath = path + ".tmp";
            lock (_statusFileLock)
            {
                Directory.CreateDirectory(ownerDirectory);
                File.WriteAllText(temporaryPath, json);
                File.Move(temporaryPath, path, true);
            }
        }
        catch (Exception error) { _logger.LogWarning(error, "无法保存项目 {ProjectId} 的训练状态", s.ProjectId); }
    }

    private static string SanitizeFileName(string id)
    {
        foreach (var c in Path.GetInvalidFileNameChars()) { id = id.Replace(c, '_'); }
        return id;
    }

    public TrainingStatus? GetStatus(string owner, string projectId, bool includeLogs = true)
    {
        if (!_statuses.TryGetValue(Key(owner, projectId), out var status)) { return null; }
        lock (status) { return status.Clone(includeLogs); }
    }

    public bool IsActive(string owner, string projectId)
    {
        if (!_statuses.TryGetValue(Key(owner, projectId), out var status)) { return false; }
        lock (status) { return status.IsActive; }
    }

    public void ForgetProject(string owner, string projectId)
    {
        var key = Key(owner, projectId);
        if (IsActive(owner, projectId)) { throw new InvalidOperationException("Cannot remove an active training project."); }
        _statuses.TryRemove(key, out _);
        _lastStatusBroadcastAt.TryRemove(key, out _);
        var statusFile = Path.Combine(StatusDir, UserStoragePath.Segment(owner), SanitizeFileName(projectId) + ".json");
        try { File.Delete(statusFile); }
        catch (Exception ex) { _logger.LogWarning(ex, "Could not delete training status for {ProjectId}", projectId); }
    }

    public async Task<TrainingStatus> StartAsync(string owner, string projectId, TrainingOptions options)
    {
        if (Volatile.Read(ref _disposed) != 0) { throw new ObjectDisposedException(nameof(TrainingService)); }
        ArgumentException.ThrowIfNullOrWhiteSpace(projectId);
        ArgumentNullException.ThrowIfNull(options);
        ValidateOptions(options);

        var project = await LoadProjectAsync(owner, projectId);
        if (project is null) { throw new InvalidOperationException("工程不存在或无权访问。"); }

        var taskType = YoloTaskRegistry.FromConfig(LabelingConfigParser.Parse(project.LabelConfigXml));
        var runOptions = CloneOptions(options);
        runOptions.Task = YoloTaskRegistry.ToCommand(taskType);
        runOptions.Model = YoloTaskRegistry.ModelFor(taskType, runOptions.Model);

        if (Interlocked.CompareExchange(ref _running, 1, 0) != 0) { throw new InvalidOperationException("已有训练在运行，请等待完成或先停止。"); }

        var key = Key(owner, projectId);
        var status = new TrainingStatus { Owner = owner, ProjectId = projectId, Phase = TrainingPhase.Preparing, TotalEpochs = runOptions.Epochs, ModelName = runOptions.Model, Message = "准备数据集…", UpdatedAt = DateTime.UtcNow };
        _statuses[key] = status;
        var cancellation = CancellationTokenSource.CreateLinkedTokenSource(_lifetimeCancellation.Token);
        if (!_runCancellations.TryAdd(key, cancellation))
        {
            cancellation.Dispose();
            Interlocked.Exchange(ref _running, 0);
            throw new InvalidOperationException("该项目的训练正在停止，请稍后重试。");
        }
        _pipelineTask = RunPipelineAsync(owner, projectId, runOptions, status, cancellation.Token);
        return status.Clone();
    }

    public async Task StopAsync(string owner, string projectId)
    {
        var key = Key(owner, projectId);
        if (_runCancellations.TryGetValue(key, out var cancellation)) { await cancellation.CancelAsync(); }
        if (_processes.TryRemove(key, out var proc)) { try { proc.Kill(true); } catch { } }
        if (_statuses.TryGetValue(key, out var s))
        {
            lock (s) { if (s.IsActive) { s.Phase = TrainingPhase.Cancelled; s.Message = "已停止"; s.UpdatedAt = DateTime.UtcNow; } }
            await PushAsync(s, persist: true);
        }
        var pipeline = Volatile.Read(ref _pipelineTask);
        if (pipeline is not null)
        {
            try { await pipeline.WaitAsync(TimeSpan.FromSeconds(15)); }
            catch (TimeoutException) { _logger.LogWarning("Training pipeline for {ProjectId} did not stop within the grace period", projectId); }
            catch (OperationCanceledException) { }
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) { return; }
        await _lifetimeCancellation.CancelAsync();
        foreach (var process in _processes.Values) { try { process.Kill(true); } catch { } }
        var pipeline = Volatile.Read(ref _pipelineTask);
        if (pipeline is not null)
        {
            try { await pipeline.WaitAsync(TimeSpan.FromSeconds(15)); }
            catch (TimeoutException) { _logger.LogWarning("Training pipeline did not stop within the shutdown grace period"); }
            catch (OperationCanceledException) { }
            catch (Exception ex) { _logger.LogWarning(ex, "Training pipeline faulted during shutdown"); }
        }
        if (pipeline is null || pipeline.IsCompleted)
        {
            foreach (var cancellation in _runCancellations.Values) { cancellation.Dispose(); }
        }
        _lifetimeCancellation.Dispose();
    }

    private static TrainingOptions CloneOptions(TrainingOptions options) => new()
    {
        Epochs = options.Epochs,
        ImgSize = options.ImgSize,
        Device = options.Device,
        Model = options.Model,
        Task = options.Task,
        UseVal = options.UseVal,
    };

    /// <summary>训练产物导出为 ONNX 并注册到验证页模型列表（供"验证模型"按钮调用）。</summary>
    public async Task<(bool Ok, string Message)> ExportForValidationAsync(string owner, string projectId)
    {
        TrainingStatus? st;
        if (!_statuses.TryGetValue(Key(owner, projectId), out st) || st is null) { return (false, "没有训练状态，请先完成训练。"); }
        var best = st.BestModelPath;
        if (string.IsNullOrEmpty(best) || !File.Exists(best)) { return (false, "未找到训练产物 best.pt，请先完成训练。"); }

        var os = DetectOs();
        var venv = TrainEnvironmentPlanner.VenvYolo(VenvRoot, os);
        // ONNX 导出 opset：YOLOv26 系列要求 opset 18，其余（YOLOv5u~YOLOv12）沿用 opset 17
        var opset = st.ModelName.Contains("yolo26", StringComparison.OrdinalIgnoreCase) ? 18 : 17;
        var exportArgs = YoloCommandBuilder.BuildExportArguments(best, opset);
        Log(st, "$ " + CommandLine.Join(venv, exportArgs), "cmd", projectId);
        var (code, so, se) = await TrainingShell.RunAsync(venv, exportArgs);
        if (code != 0)
        {
            var err = LastNonEmpty(se, so);
            Log(st, "ONNX 导出失败：" + err, "err", projectId);
            return (false, "模型导出失败：" + err);
        }
        var onnxPath = Path.Combine(Path.GetDirectoryName(best)!, Path.GetFileNameWithoutExtension(best) + ".onnx");
        if (!File.Exists(onnxPath)) { return (false, "导出完成但未找到 onnx 文件：" + onnxPath); }

        var project = await LoadProjectAsync(owner, projectId);
        using var scope = _scopeFactory.CreateScope();
        var valid = scope.ServiceProvider.GetRequiredService<ValidationService>();
        using var fs = File.OpenRead(onnxPath);
        var type = OnnxTypeOf(project?.LabelConfigXml);
        var r = await valid.AddModelForOwnerAsync(owner, fs, (project?.Name ?? "model") + "-best.onnx", "训练完成 " + st.ModelName + " · " + st.YoloVersion, type);
        if (!r.Status) { return (false, "注册模型失败：" + r.Message); }
        Log(st, "模型已导出并注册到验证模型列表", "out", projectId);
        return (true, "模型已导出并加入验证模型列表");
    }

    private static OnnxType OnnxTypeOf(string? xml)
    {
        try
        {
            var t = Snet.Yolo.Tasks.Core.Config.YoloTaskRegistry.FromConfig(Snet.Yolo.Tasks.Core.Config.LabelingConfigParser.Parse(xml ?? ""));
            return t switch
            {
                Snet.Yolo.Tasks.Core.Config.YoloTaskType.Segment => OnnxType.Segmentation,
                Snet.Yolo.Tasks.Core.Config.YoloTaskType.Classify => OnnxType.Classification,
                Snet.Yolo.Tasks.Core.Config.YoloTaskType.Pose => OnnxType.PoseEstimation,
                Snet.Yolo.Tasks.Core.Config.YoloTaskType.Obb => OnnxType.ObbDetection,
                _ => OnnxType.ObjectDetection,
            };
        }
        catch { return OnnxType.ObjectDetection; }
    }

    private async Task<WorkspaceProject?> LoadProjectAsync(string owner, string projectId, CancellationToken cancellationToken = default)
    {
        using var scope = _scopeFactory.CreateScope();
        var workspaces = scope.ServiceProvider.GetRequiredService<WorkspaceService>();
        return await workspaces.GetProjectForOwnerAsync(owner, projectId, cancellationToken);
    }

    private async Task RunPipelineAsync(string owner, string projectId, TrainingOptions options, TrainingStatus status, CancellationToken cancellationToken)
    {
        var key = Key(owner, projectId);
        try
        {
            var project = await LoadProjectAsync(owner, projectId, cancellationToken);
            if (project is null) { await Fail(status, "工程不存在", projectId); return; }

            var cfg = LabelingConfigParser.Parse(project.LabelConfigXml);
            var ytask = YoloTaskRegistry.FromConfig(cfg);
            options.Task = YoloTaskRegistry.ToCommand(ytask);
            options.Model = YoloTaskRegistry.ModelFor(ytask, options.Model);
            lock (status) { status.ModelName = options.Model; }

            Set(status, TrainingPhase.Preparing, "导出 YOLO 数据集…");
            var envRoot = Path.Combine(AppContext.BaseDirectory, "train");
            Directory.CreateDirectory(envRoot);
            var projectDir = Path.Combine(envRoot, "users", UserStoragePath.Segment(owner), SanitizeFileName(project.Id));
            Log(status, "导出 YOLO 数据集到 " + projectDir + " ……", "out", projectId);
            var dataYaml = WriteDataset(owner, project, projectDir, options, cancellationToken);
            Log(status, "数据集已导出：" + dataYaml, "out", projectId);

            Set(status, TrainingPhase.EnvironmentCheck, "检测训练环境…");
            var snap = await DetectEnvironmentAsync(status, projectId, cancellationToken);
            var plan = TrainEnvironmentPlanner.Plan(snap);

            if (!plan.EnvReady)
            {
                Set(status, TrainingPhase.Installing, "搭建训练环境…");
                foreach (var step in plan.Steps)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    Set(status, TrainingPhase.Installing, step.Description);
                    if (string.IsNullOrEmpty(step.Command.Executable)) { await Fail(status, step.Description, projectId); return; }
                    // venv 目录必须先整目录删除：python -m venv 会复用残留目录，无法修复半损坏环境
                    if (step.Kind == SetupStepKind.RecreateVenv)
                    {
                        try { VenvRebuilder.Reset(VenvRoot); }
                        catch (Exception error)
                        {
                            Log(status, error.Message, "err", projectId);
                            await Fail(status, error.Message, projectId); return;
                        }
                    }
                    if (!await RunSetupStepAsync(step, status, projectId, cancellationToken)) { return; }
                }
            }

            var (acceleratorAvailable, yoloVer) = await ResolveRuntimeAsync(plan, snap.Os, status, projectId, cancellationToken);
            // macOS：是否有 MPS 由运行时探测决定（首次安装前无法预知）；Windows/Linux 需要计划与运行时同时判定为 GPU
            var accelerated = snap.Os == OsKind.Mac ? acceleratorAvailable : plan.UseGpu && acceleratorAvailable;
            var device = TorchRuntimeProbe.SelectDevice(snap.Os, accelerated);
            if (device == "cpu") { var warn = "当前走 CPU 训练，速度较慢、效率较低。"; Set(status, TrainingPhase.Training, warn); Log(status, warn, "warn", projectId); }
            options.Device = device;

            Set(status, TrainingPhase.Training, "开始训练…");
            status.YoloVersion = yoloVer; status.Device = device; status.GpuName = snap.Gpu?.Name ?? "";
            var trainArgs = YoloCommandBuilder.BuildTrainArguments(dataYaml, options);
            Log(status, "$ " + CommandLine.Join(plan.VenvYolo, trainArgs), "cmd", projectId);

            var exit = await RunTrainProcessAsync(key, projectId, status, plan.VenvYolo, trainArgs, projectDir, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            if (exit == 0)
            {
                var best = Directory.GetFiles(projectDir, "best.pt", SearchOption.AllDirectories).FirstOrDefault();
                lock (status) { status.BestModelPath = best ?? string.Empty; status.Percent = 100; }
                // 训练收尾不再自动跑 yolo val（验证改由"验证模型"一键导出 ONNX 到验证页完成）
                Set(status, TrainingPhase.Complete, "训练完成");
            }
            else
            {
                List<string> logSnapshot;
                lock (status) { logSnapshot = new List<string>(status.LogTail); }
                var reason = SummarizeError(logSnapshot) ?? "退出码 " + exit;
                await Fail(status, "训练失败：" + reason, projectId);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            lock (status)
            {
                if (status.IsActive) { status.Phase = TrainingPhase.Cancelled; status.Message = "已停止"; status.UpdatedAt = DateTime.UtcNow; }
            }
            await PushAsync(status, persist: true);
        }
        catch (Exception ex) { await Fail(status, "训练出错：" + ex.Message, projectId); }
        finally
        {
            _processes.TryRemove(key, out _);
            if (_runCancellations.TryRemove(key, out var cancellation)) { cancellation.Dispose(); }
            _lastStatusBroadcastAt.TryRemove(key, out _);
            Interlocked.Exchange(ref _running, 0);
        }
    }

    /// <summary>
    /// 执行一条搭建步骤：网络 pip 步骤走“按计划 -> 禁用代理直连”的重试阶梯，
    /// 每次尝试的 argv、子进程环境、输出与结果都写入训练日志。全部失败时返回 false 并已置为 Failed。
    /// </summary>
    private async Task<bool> RunSetupStepAsync(SetupStep step, TrainingStatus status, string projectId, CancellationToken cancellationToken)
    {
        var attempts = step.IsNetwork ? PipProxyPolicy.MaxAttempts : 1;
        for (var attempt = 1; attempt <= attempts; attempt++)
        {
            var arguments = step.IsNetwork
                ? PipProxyPolicy.BuildArguments(step.Command.ArgumentList, attempt, _configuredProxy)
                : step.Command.ArgumentList;
            var environment = PipProxyPolicy.BuildEnvironmentOverrides(attempt);

            if (attempt > 1) { Log(status, "第 " + attempt + " 次尝试：" + PipProxyPolicy.DescribeAttempt(attempt), "warn", projectId); }
            else if (step.IsNetwork) { Log(status, "第 1 次尝试：" + PipProxyPolicy.DescribeAttempt(1), "out", projectId); }
            Log(status, "$ " + CommandLine.Join(step.Command.Executable, arguments), "cmd", projectId);

            var (code, stdout, stderr) = await TrainingShell.RunAsync(step.Command.Executable, arguments, null, environment, cancellationToken);
            if (stdout.Length > 0) { Log(status, stdout, "out", projectId); }
            if (stderr.Length > 0) { Log(status, stderr, "err", projectId); }
            if (code == 0) { return true; }

            var detail = LastNonEmpty(stderr, stdout);
            if (attempt >= attempts)
            {
                var message = step.IsNetwork
                    ? PipProxyPolicy.FailureMessage(step.Description) + "（退出码 " + code + "）" + detail
                    : "环境搭建失败（退出码 " + code + "）：" + detail;
                Log(status, message, "err", projectId);
                await Fail(status, message, projectId);
                return false;
            }
            Log(status, "安装失败（退出码 " + code + "）：" + detail, "warn", projectId);
        }
        return false;
    }

    private static string ClassifyOf(AnnotationTask task)
    {
        var ann = task.Annotations.FirstOrDefault(a => a.WasCancelled != true);
        if (ann is not null)
        {
            foreach (var row in ann.Result)
            {
                if (row.Value is null) { continue; }
                var label = ValueAccess.GetStringList(row.Value, "choices").FirstOrDefault();
                if (!string.IsNullOrEmpty(label)) { return label; }
            }
        }
        // 分类文件夹流程：导入时类别存于 Data["class"]
        return task.Data?["class"]?.ToString() ?? string.Empty;
    }
    private string WriteDataset(string owner, WorkspaceProject project, string projectDir, TrainingOptions options, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Directory.CreateDirectory(projectDir);
        var config = LabelingConfigParser.Parse(project.LabelConfigXml);
        var taskType = YoloTaskRegistry.FromConfig(config);
        var classes = config.Controls.SelectMany(c => c.Labels).Select(l => l.Value).Distinct().ToList();

        var useVal = options.UseVal;
        var imagesDir = Path.Combine(projectDir, "images");
        var labelsDir = Path.Combine(projectDir, "labels");
        string? valImagesDir = null, valLabelsDir = null;
        if (useVal)
        {
            valImagesDir = Path.Combine(projectDir, "val", "images");
            valLabelsDir = Path.Combine(projectDir, "val", "labels");
        }
        // 重建数据集：先清空旧目录，避免上一轮任务残留（如改类型/改标注后的旧格式标签）
        void ResetDir(string d) { if (Directory.Exists(d)) { Directory.Delete(d, true); } Directory.CreateDirectory(d); }
        ResetDir(imagesDir); ResetDir(labelsDir);
        if (useVal) { ResetDir(valImagesDir!); ResetDir(valLabelsDir!); }

        var uploads = Path.Combine(AppContext.BaseDirectory, "wwwroot", "data", "uploads", UserStoragePath.Segment(owner), project.Id);
        var tasks = project.Tasks
            .Where(task => !string.IsNullOrEmpty(task.Data?["image"]?.ToString()))
            .OrderBy(task => StableSplitKey(project.Id, task))
            .ToList();
        var valCount = useVal && tasks.Count > 1 ? Math.Clamp((int)Math.Round(tasks.Count * 0.1), 1, tasks.Count - 1) : 0;

        // 图像分类：每类一个文件夹（Ultralytics classify 数据集结构）
        if (taskType == YoloTaskType.Classify)
        {
            var trainRoot = Path.Combine(projectDir, "train");
            var valRoot = Path.Combine(projectDir, "val");
            if (Directory.Exists(trainRoot)) { Directory.Delete(trainRoot, true); }
            if (Directory.Exists(valRoot)) { Directory.Delete(valRoot, true); }
            Directory.CreateDirectory(trainRoot);
            if (useVal) { Directory.CreateDirectory(valRoot); }
            var classified = 0;
            var cid = 0;
            var classDirectories = classes
                .GroupBy(SanitizeName, StringComparer.OrdinalIgnoreCase)
                .Where(group => group.Count() > 1)
                .Select(group => group.Key)
                .ToArray();
            if (classDirectories.Length > 0)
            {
                throw new InvalidOperationException("分类名称在文件系统清理后发生冲突: " + string.Join(", ", classDirectories));
            }
            var validationTasks = new HashSet<AnnotationTask>();
            if (useVal)
            {
                foreach (var group in tasks.Where(task => !string.IsNullOrEmpty(ClassifyOf(task))).GroupBy(ClassifyOf, StringComparer.Ordinal))
                {
                    var count = group.Count();
                    if (count < 2) { continue; }
                    var take = Math.Clamp((int)Math.Round(count * 0.1), 1, count - 1);
                    foreach (var task in group.Take(take)) { validationTasks.Add(task); }
                }
            }
            foreach (var task in tasks)
            {
                cancellationToken.ThrowIfCancellationRequested();
                cid++;
                var cls = ClassifyOf(task);
                if (string.IsNullOrEmpty(cls)) { continue; }
                var toVal = validationTasks.Contains(task);
                var root = toVal ? valRoot : trainRoot;
                var clsDir = Path.Combine(root, SanitizeName(cls));
                Directory.CreateDirectory(clsDir);
                var imgName = ExportService.ResolveFileName(task.Data!["image"]!.ToString());
                var srcImg = Path.Combine(uploads, imgName);
                if (!File.Exists(srcImg)) { throw new FileNotFoundException("训练图片不存在。", srcImg); }
                File.Copy(srcImg, Path.Combine(clsDir, cid + Path.GetExtension(imgName)), true);
                classified++;
            }
            if (classified == 0) { throw new InvalidOperationException("导出数据集中没有任何分类标注，请先在标注器里为图片选择分类。"); }
            var hasVal = useVal && Directory.Exists(valRoot) && Directory.EnumerateFiles(valRoot, "*", SearchOption.AllDirectories).Any();
            var yaml = "path: " + projectDir.Replace('\\', '/') + "\ntrain: train\nval: " + (hasVal ? "val" : "train") + "\nnames: [" + string.Join(", ", classes.Select(x => "\"" + x.Replace("\"", "\\\"") + "\"")) + "]\n";
            var yamlPath = Path.Combine(projectDir, "data.yaml");
            File.WriteAllText(yamlPath, yaml);
            return projectDir; // 分类数据集用目录(Ultralytics classify)而非 yaml
        }

        var id = 0;
        foreach (var task in tasks)
        {
            cancellationToken.ThrowIfCancellationRequested();
            id++;
            var toVal = useVal && id <= valCount;
            var imageRef = task.Data!["image"]!.ToString();
            var imgName = ExportService.ResolveFileName(imageRef);
            var ext = Path.GetExtension(imgName);
            var src = Path.Combine(uploads, imgName);
            var imgTargetDir = toVal ? valImagesDir! : imagesDir;
            var lblTargetDir = toVal ? valLabelsDir! : labelsDir;
            if (!File.Exists(src)) { throw new FileNotFoundException("训练图片不存在。", src); }
            File.Copy(src, Path.Combine(imgTargetDir, id + ext), true);
            var labelText = YoloLabelExporter.Build(task, taskType, classes);
            if (!string.IsNullOrWhiteSpace(labelText)) { File.WriteAllText(Path.Combine(lblTargetDir, id + ".txt"), labelText); }
        }

        File.WriteAllText(Path.Combine(projectDir, "classes.txt"), string.Join("\n", classes.Select((c, idx) => idx + " " + c)) + "\n");

        var totalLabels = Directory.EnumerateFiles(labelsDir, "*.txt").Count() + (valLabelsDir is not null && Directory.Exists(valLabelsDir) ? Directory.EnumerateFiles(valLabelsDir, "*.txt").Count() : 0);
        if (totalLabels == 0)
        {
            throw new InvalidOperationException("导出数据集中没有任何标注（图片数 " + tasks.Count + "），请先在标注器里为图片添加标注后再训练。");
        }
        var totalImages = Directory.EnumerateFiles(imagesDir).Count()
            + (valImagesDir is not null && Directory.Exists(valImagesDir) ? Directory.EnumerateFiles(valImagesDir).Count() : 0);
        if (totalImages != tasks.Count)
        {
            throw new InvalidOperationException($"训练数据集不完整：期望 {tasks.Count} 张图片，实际导出 {totalImages} 张。");
        }

        var valExists = useVal && valImagesDir is not null && Directory.Exists(valImagesDir) && Directory.EnumerateFiles(valImagesDir).Any();

        // 姿态训练：data.yaml 需要 kpt_shape（每行对象的关键点数量），并校验各目标点数一致
        var kptCount = 0;
        if (taskType == YoloTaskType.Pose)
        {
            var counts = new List<int>();
            foreach (var dir in new[] { labelsDir, valLabelsDir }.Where(d => d is not null && Directory.Exists(d)))
            {
                foreach (var f in Directory.EnumerateFiles(dir!, "*.txt"))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    foreach (var raw in File.ReadAllLines(f))
                    {
                        var vals = raw.Trim().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
                        if (vals.Length < 5) { continue; }
                        counts.Add((vals.Length - 5) / 3); // 姿态行 = 5 列 bbox 前缀 + n×(x y v)
                    }
                }
            }
            if (counts.Count == 0) { throw new InvalidOperationException("姿态数据集中没有任何关键点标注，请先在标注器里为图片添加关键点后再训练。"); }
            var distinct = counts.Distinct().ToList();
            if (distinct.Count != 1) { throw new InvalidOperationException("姿态关键点数量不一致（检测到 " + string.Join("/", distinct) + " 种点数），请保证每个目标的关键点数量一致后再训练。"); }
            kptCount = distinct[0];
        }

        var dataYaml = DataYamlBuilder.Build(classes, projectDir, useVal, valExists, kptCount);
        var dataYamlPath = Path.Combine(projectDir, "data.yaml");
        File.WriteAllText(dataYamlPath, dataYaml);
        return dataYamlPath;
    }

    /// <summary>按当前操作系统判定 OsKind（macOS 不能当成 Linux 处理：torch wheel 索引与设备都不同）。</summary>
    private static OsKind DetectOs()
        => OperatingSystem.IsWindows() ? OsKind.Windows : OperatingSystem.IsMacOS() ? OsKind.Mac : OsKind.Linux;

    /// <summary>解释器/venv 版本查询超时。</summary>
    private static readonly TimeSpan VersionProbeTimeout = TimeSpan.FromSeconds(15);
    /// <summary>venv 内 pip 查询超时（冷启动文件系统上会偏慢）。</summary>
    private static readonly TimeSpan PipProbeTimeout = TimeSpan.FromSeconds(45);
    /// <summary>nvidia-smi 超时：驱动异常时它会一直不返回。</summary>
    private static readonly TimeSpan GpuProbeTimeout = TimeSpan.FromSeconds(8);
    /// <summary>torch 运行时探测超时（首次导入较慢，留足余量）。</summary>
    private static readonly TimeSpan RuntimeProbeTimeout = TimeSpan.FromSeconds(120);

    /// <summary>
    /// 检测训练环境。每一步都先写日志再执行并带超时，
    /// 因此不会再出现“界面停在检测环境、日志一片空白、也不知道卡在哪条命令”的情况。
    /// </summary>
    private async Task<TrainingEnvSnapshot> DetectEnvironmentAsync(TrainingStatus status, string projectId, CancellationToken cancellationToken)
    {
        var os = DetectOs();
        var snap = new TrainingEnvSnapshot { Os = os };
        Log(status, "开始检测训练环境（系统：" + os + "）……", "out", projectId);

        // Python 解释器探测：按优先级取第一个可用的 Python 3（Windows: python / py -3；Linux+macOS: python3 / python）
        foreach (var probe in PythonDiscovery.VersionProbes(os))
        {
            var version = await ProbeAsync(status, projectId, probe.Launcher.Executable, probe.Arguments, VersionProbeTimeout, cancellationToken);
            if (version.Item1 != 0 || !PythonDiscovery.IsPython3(version.Item2, version.Item3)) { continue; }
            snap.PythonCmd = probe.Launcher.Executable;
            snap.PythonArguments = probe.Launcher.PrefixArguments;
            snap.HasPython = true;
            Log(status, "已选择 Python：" + CommandLine.Join(probe.Launcher.Executable, probe.Launcher.PrefixArguments) + " → " + FirstLine(version.Item3, version.Item2), "out", projectId);
            break;
        }
        if (!snap.HasPython) { Log(status, "未找到可用的 Python 3。", "warn", projectId); }

        if (snap.HasPython)
        {
            var launcher = new PythonLauncher(snap.PythonCmd, snap.PythonArguments);
            // venv 能力必须单独校验：Debian/Ubuntu 的 python3 默认不带 venv 模块
            snap.HasVenv = (await ProbeAsync(status, projectId, snap.PythonCmd, launcher.WithArguments(PythonDiscovery.VenvProbeArguments), VersionProbeTimeout, cancellationToken)).Item1 == 0;
            snap.HasPip = (await ProbeAsync(status, projectId, snap.PythonCmd, launcher.WithArguments(new[] { "-m", "pip", "--version" }), VersionProbeTimeout, cancellationToken)).Item1 == 0;
            Log(status, "系统 Python 能力：venv 模块 " + (snap.HasVenv ? "可用" : "缺失") + "，pip " + (snap.HasPip ? "可用" : "缺失"), snap.HasVenv && snap.HasPip ? "out" : "warn", projectId);
        }

        snap.Gpu = await DetectNvidiaGpuAsync(os, status, projectId, cancellationToken);
        Log(status, snap.Gpu is { HasGpu: true } detectedGpu
            ? "检测到 GPU：" + detectedGpu.Name + "（驱动 " + detectedGpu.DriverVersion + "，计算能力 " + (string.IsNullOrWhiteSpace(detectedGpu.ComputeCap) ? "未知，按 cu121 保守处理" : detectedGpu.ComputeCap) + "）"
            : "未检测到 NVIDIA GPU，将按 CPU 训练。", snap.Gpu is { HasGpu: true } ? "out" : "warn", projectId);

        var venvRoot = VenvRoot;
        snap.VenvPath = venvRoot;
        var venvPython = TrainEnvironmentPlanner.VenvPython(venvRoot, os);
        snap.VenvDirectoryExists = Directory.Exists(venvRoot);
        snap.VenvExists = File.Exists(venvPython);
        if (snap.VenvExists)
        {
            snap.VenvHasTorch = (await ProbeAsync(status, projectId, venvPython, new[] { "-m", "pip", "show", "torch" }, PipProbeTimeout, cancellationToken)).Item1 == 0;
            snap.VenvHasUltralytics = (await ProbeAsync(status, projectId, venvPython, new[] { "-m", "pip", "show", "ultralytics" }, PipProbeTimeout, cancellationToken)).Item1 == 0;
            if (os == OsKind.Mac && snap.VenvHasTorch) { snap.HasMps = await DetectMpsAsync(venvPython, status, projectId, cancellationToken); }
        }
        Log(status, "训练环境目录：" + (snap.VenvExists ? "已存在" : "不存在")
            + "（torch " + (snap.VenvHasTorch ? "已安装" : "未安装") + "，ultralytics " + (snap.VenvHasUltralytics ? "已安装" : "未安装") + "）", "out", projectId);
        snap.TorchInstalled = snap.VenvHasTorch; snap.UltralyticsInstalled = snap.VenvHasUltralytics;
        return snap;
    }

    /// <summary>
    /// 运行一条探测命令：先写 [cmd] 日志再执行，并限制最长执行时间。
    /// 探测失败/超时只记录结果，绝不抛出（调用方取消除外），保证检测阶段不会永久卡住。
    /// </summary>
    private async Task<(int ExitCode, string Stdout, string Stderr)> ProbeAsync(
        TrainingStatus status,
        string projectId,
        string file,
        IReadOnlyList<string> arguments,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        var commandText = CommandLine.Join(file, arguments);
        Log(status, "$ " + commandText, "cmd", projectId);
        var result = await TrainingShell.RunAsync(file, arguments, timeout, cancellationToken);
        if (result.Item1 == TrainingShell.TimeoutExitCode)
        {
            Log(status, "探测超时（" + timeout.TotalSeconds.ToString("0") + " 秒），已跳过并继续检测其他项：" + commandText
                + "。该命令没有返回（常见原因：显卡驱动异常、网络代理无响应、文件系统挂起），如反复出现请先在服务器上手动执行这条命令确认。", "warn", projectId);
        }
        else if (result.Item1 != 0 && !TrainingShell.IsStartFailure(result.Item1, result.Item3))
        {
            Log(status, "探测失败（退出码 " + result.Item1 + "）：" + FirstLine(result.Item3, result.Item2), "warn", projectId);
        }
        return result;
    }

    /// <summary>取输出中第一行非空文本，用于单行日志。</summary>
    private static string FirstLine(string primary, string fallback)
    {
        foreach (var candidate in new[] { primary, fallback })
        {
            if (string.IsNullOrWhiteSpace(candidate)) { continue; }
            var line = candidate.Split('\n').Select(text => text.Trim()).FirstOrDefault(text => text.Length > 0);
            if (line is null) { continue; }
            return line.Length > 200 ? line[..200] + "…" : line;
        }
        return string.Empty;
    }

    /// <summary>
    /// 探测 NVIDIA GPU：先试 PATH 中的 nvidia-smi，再试常见绝对路径（systemd 精简 PATH、WSL、Windows System32）；
    /// 完整查询失败时降级为不含 compute_cap 的查询。任何失败都不抛出，仅返回 null。
    /// </summary>
    private async Task<GpuInfo?> DetectNvidiaGpuAsync(OsKind os, TrainingStatus status, string projectId, CancellationToken cancellationToken)
    {
        foreach (var candidate in NvidiaSmi.CandidateExecutables(os))
        {
            try
            {
                var rich = await ProbeAsync(status, projectId, candidate, NvidiaSmi.RichQuery, GpuProbeTimeout, cancellationToken);
                if (rich.Item1 == TrainingShell.TimeoutExitCode) { return AbortGpuDetection(status, projectId); }
                if (rich.Item1 == 0)
                {
                    var gpus = NvidiaSmiParser.ParseCsv(rich.Item2);
                    if (gpus.Count > 0) { return gpus[0]; }
                }
                // 旧驱动没有 compute_cap 字段：降级查询仍视为可用 GPU（CUDA 通道保守选择 cu121）
                var legacy = await ProbeAsync(status, projectId, candidate, NvidiaSmi.LegacyQuery, GpuProbeTimeout, cancellationToken);
                if (legacy.Item1 == TrainingShell.TimeoutExitCode) { return AbortGpuDetection(status, projectId); }
                if (legacy.Item1 == 0)
                {
                    var gpus = NvidiaSmiParser.ParseCsv(legacy.Item2);
                    if (gpus.Count > 0)
                    {
                        if (rich.Item1 != 0) { _logger.LogInformation("nvidia-smi 不支持 compute_cap 查询，已降级为 {Candidate}", candidate); }
                        return gpus[0];
                    }
                }
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception error) { _logger.LogDebug(error, "nvidia-smi 探测失败：{Candidate}", candidate); }
        }
        return null;
    }

    /// <summary>nvidia-smi 无响应时放弃 GPU 检测：继续试其余候选路径只会继续卡住。</summary>
    private GpuInfo? AbortGpuDetection(TrainingStatus status, string projectId)
    {
        Log(status, "nvidia-smi 无响应，已放弃 GPU 检测（本次按 CPU 训练）。若服务器确实装了显卡，请先手动执行 nvidia-smi 排查驱动状态。", "warn", projectId);
        return null;
    }

    /// <summary>macOS：仅当 venv 内已安装 torch 时探测 Apple MPS 是否可用。</summary>
    private async Task<bool> DetectMpsAsync(string venvPython, TrainingStatus status, string projectId, CancellationToken cancellationToken)
    {
        try
        {
            var result = await ProbeAsync(status, projectId, venvPython, TorchRuntimeProbe.MpsProbeArguments, RuntimeProbeTimeout, cancellationToken);
            if (result.Item1 != 0) { return false; }
            return (result.Item2 + "\n" + result.Item3).Split('\n').Any(line => line.Trim().Equals("True", StringComparison.OrdinalIgnoreCase));
        }
        catch (OperationCanceledException) { throw; }
        catch { return false; }
    }

    /// <summary>运行 venv 内的探测脚本，返回（是否具备硬件加速后端，ultralytics 版本）。</summary>
    private async Task<(bool Accelerator, string YoloVersion)> ResolveRuntimeAsync(SetupPlan plan, OsKind os, TrainingStatus status, string projectId, CancellationToken cancellationToken)
    {
        if (!File.Exists(plan.VenvPython)) { return (false, "未知"); }
        var arguments = TorchRuntimeProbe.Arguments(os);
        var result = await ProbeAsync(status, projectId, plan.VenvPython, arguments, RuntimeProbeTimeout, cancellationToken);
        if (result.Item1 == TrainingShell.TimeoutExitCode) { return (false, "超时"); }
        if (result.Item1 != 0) { return (false, "未安装"); }
        var lines = (result.Item2 + "\n" + result.Item3).Split('\n', StringSplitOptions.RemoveEmptyEntries);
        var accelerator = lines.Any(l => l.Trim().Equals("True", StringComparison.OrdinalIgnoreCase));
        var ver = lines.Select(l => l.Trim()).FirstOrDefault(l => System.Text.RegularExpressions.Regex.IsMatch(l, @"^\d+\.\d+\.\d+$")) ?? "未知";
        Log(status, "运行时探测：硬件加速 " + (accelerator ? "可用" : "不可用") + "，ultralytics 版本 " + ver, "out", projectId);
        return (accelerator, ver);
    }

    private async Task<int> RunTrainProcessAsync(string key, string projectId, TrainingStatus status, string executable, IReadOnlyList<string> arguments, string workDir, CancellationToken cancellationToken)
    {
        var psi = new ProcessStartInfo
        {
            FileName = executable,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            WorkingDirectory = workDir,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };
        // 参数列表直传：路径含空格（如安装目录 C:\Program Files\...）也不会被拆错
        foreach (var argument in arguments) { psi.ArgumentList.Add(argument); }
        psi.Environment["PYTHONUTF8"] = "1";
        psi.Environment["PYTHONIOENCODING"] = "utf-8";
        Process? proc;
        try { proc = Process.Start(psi); }
        catch (Exception error) { await Fail(status, "无法启动训练进程：" + error.Message, projectId); return -1; }
        if (proc is null) { await Fail(status, "无法启动训练进程", projectId); return -1; }
        _processes[key] = proc;
        using (proc)
        {
            // 关闭标准输入：训练脚本里的交互式确认（例如“是否自动安装依赖 y/n”）拿到 EOF 后
            // 会按非交互模式继续，而不是让训练永久停在那里等一个永远不会有人的输入。
            try { proc.StandardInput.Close(); } catch { }

            var so = new StringBuilder(); var se = new StringBuilder();
            proc.OutputDataReceived += (_, e) => { if (e.Data is not null) OnTrainLine(projectId, status, e.Data); };
            proc.ErrorDataReceived += (_, e) => { if (e.Data is not null) OnTrainLine(projectId, status, e.Data); };
            proc.BeginOutputReadLine(); proc.BeginErrorReadLine();
            try
            {
                try { await proc.WaitForExitAsync(cancellationToken); }
                catch (OperationCanceledException)
                {
                    try { proc.Kill(entireProcessTree: true); } catch { }
                    try { await proc.WaitForExitAsync(CancellationToken.None); } catch { }
                    throw;
                }
                return proc.ExitCode;
            }
            finally { _processes.TryRemove(key, out _); }
        }
    }

    private void OnTrainLine(string projectId, TrainingStatus status, string line)
    {
        Log(status, line, "out", projectId);
        var u = YoloOutputParser.Parse(line);
        if (u is not null) lock (status) { if (u.Epoch is not null) status.Epoch = u.Epoch.Value; if (u.TotalEpochs is > 0) status.TotalEpochs = u.TotalEpochs.Value; status.Percent = u.Percent; if (u.BoxLoss is not null) status.Metrics.BoxLoss = u.BoxLoss; if (u.ClsLoss is not null) status.Metrics.ClsLoss = u.ClsLoss; if (u.DflLoss is not null) status.Metrics.DflLoss = u.DflLoss; }
        var m = YoloOutputParser.ParseMetrics(line);
        if (m is not null) lock (status) { status.Metrics.Precision = m.Precision; status.Metrics.Recall = m.Recall; status.Metrics.Map50 = m.Map50; status.Metrics.Map5095 = m.Map5095; }
        if (u is not null || m is not null) { QueueStatusBroadcast(status); }
    }

    private void Set(TrainingStatus s, TrainingPhase p, string msg)
    {
        lock (s) { s.Phase = p; s.Message = msg; s.UpdatedAt = DateTime.UtcNow; }
        _ = PushSafelyAsync(s, persist: true);
    }

    private void Log(TrainingStatus s, string text, string level, string projectId)
    {
        if (string.IsNullOrWhiteSpace(text)) { return; }
        lock (s)
        {
            s.LogTail.Add("[" + level + "] " + text.TrimEnd());
            if (s.LogTail.Count > 300) { s.LogTail.RemoveRange(0, s.LogTail.Count - 300); }
        }
        _ = PushLogSafelyAsync(s.Owner, projectId, text, level);
    }

    private void QueueStatusBroadcast(TrainingStatus status)
    {
        var now = Environment.TickCount64;
        while (true)
        {
            var statusKey = Key(status.Owner, status.ProjectId);
            var previous = _lastStatusBroadcastAt.GetOrAdd(statusKey, 0);
            if (now - previous < StatusBroadcastIntervalMs) { return; }
            if (_lastStatusBroadcastAt.TryUpdate(statusKey, now, previous)) { break; }
        }
        _ = PushSafelyAsync(status, persist: false);
    }

    private async Task PushSafelyAsync(TrainingStatus status, bool persist)
    {
        try { await PushAsync(status, persist); }
        catch (Exception error) { _logger.LogWarning(error, "无法推送项目 {ProjectId} 的训练状态", status.ProjectId); }
    }

    private async Task PushLogSafelyAsync(string owner, string projectId, string text, string level)
    {
        try { await TrainingHub.PushLog(_hub, owner, projectId, text, level); }
        catch (Exception error) { _logger.LogWarning(error, "无法推送项目 {ProjectId} 的训练日志", projectId); }
    }

    private Task PushAsync(TrainingStatus status, bool persist)
    {
        if (persist) { SaveStatus(status); }
        TrainingStatus snapshot;
        lock (status) { snapshot = status.Clone(includeLogs: false); }
        return TrainingHub.PushStatus(_hub, status.Owner, status.ProjectId, snapshot);
    }
    /// <summary>训练日志常见错误 -> 友好中文提示（供失败时归纳原因）。</summary>
    private static readonly (string Pattern, string Hint)[] TrainErrorHints = new[]
    {
        ("No `kpt_shape`", "姿态数据集缺少关键点定义(kpt_shape)，请确认标注后再训练。"),
        ("labels require", "关键点列数与声明不一致：请保证每张图的目标关键点数量一致（缺失点需补齐）后重试。"),
        ("corrupt image/label", "存在损坏的标签行（关键点数量与声明不一致），已按提示修正后重试。"),
        ("No valid images found", "数据集未通过校验：请检查图片与标签是否成对、关键点数量是否一致。"),
        ("CUDA out of memory", "显存不足：请调小 imgsz / batch，或换更小的模型后重试。"),
        ("No such file or directory", "文件路径不存在：请确认项目数据完整后重试。"),
        ("error", "训练脚本报错，详见上方日志。"),
    };

    /// <summary>从日志尾部逆向匹配已知错误，返回最贴近的中文原因。</summary>
    private static string? SummarizeError(IReadOnlyList<string> tail)
    {
        for (var i = tail.Count - 1; i >= 0 && i >= tail.Count - 40; i--)
        {
            foreach (var (pattern, hint) in TrainErrorHints)
            {
                if (tail[i].Contains(pattern, StringComparison.OrdinalIgnoreCase)) { return hint; }
            }
        }
        return null;
    }

    private Task Fail(TrainingStatus s, string msg, string projectId) { lock (s) { s.Phase = TrainingPhase.Failed; s.Message = msg; s.LastError = msg; s.UpdatedAt = DateTime.UtcNow; } return PushAsync(s, persist: true); }

    private static string LastNonEmpty(string a, string b) => (!string.IsNullOrWhiteSpace(a) ? a.Trim().Split('\n').LastOrDefault() : null) ?? (!string.IsNullOrWhiteSpace(b) ? b.Trim().Split('\n').LastOrDefault() : null) ?? "未知";
    private static string SanitizeName(string name) { var bad = Path.GetInvalidFileNameChars(); var s = new string(name.Select(c => bad.Contains(c) ? '_' : c).ToArray()).Trim(); return string.IsNullOrEmpty(s) ? "project" : s; }
    /// <summary>验证训练参数边界，避免无效或失控的训练进程。</summary>
    private static void ValidateOptions(TrainingOptions options)
    {
        if (options.Epochs is < 1 or > 10_000) { throw new ArgumentOutOfRangeException(nameof(options), "训练轮数必须在 1 到 10000 之间。"); }
        if (options.ImgSize is < 32 or > 4096 || options.ImgSize % 32 != 0) { throw new ArgumentOutOfRangeException(nameof(options), "图像尺寸必须是 32 到 4096 之间的 32 倍数。"); }
        if (!System.Text.RegularExpressions.Regex.IsMatch(options.Model ?? string.Empty, @"^yolo(?:11|26)[nslmx](?:-(?:seg|cls|pose|obb))?\.pt$", System.Text.RegularExpressions.RegexOptions.CultureInvariant))
        {
            throw new ArgumentException("模型名称不在允许的官方模型列表中。", nameof(options));
        }
    }

    /// <summary>生成跨进程稳定的任务排序键，避免按上传顺序切分验证集。</summary>
    private static string StableSplitKey(string projectId, AnnotationTask task)
    {
        var source = projectId + "|" + (task.Id?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? task.Data?["image"]?.ToString());
        return Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(source)));
    }
    private static string Key(string owner, string projectId) => owner + "\n" + projectId;
}
