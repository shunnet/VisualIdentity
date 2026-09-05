using Microsoft.AspNetCore.SignalR;
using Snet.Yolo.Tasks.Core.Config;
using Snet.Yolo.Tasks.Core.Models;
using Snet.Yolo.Tasks.Core.Editing;
using Snet.Yolo.Tasks.Core.Serialization.Export;
using Snet.Yolo.Tasks.Core.Training;
using Snet.Yolo.Tasks.Core.Workspace;
using Snet.Yolo.Server.models.@enum;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;

namespace Snet.Yolo.Tasks.Services;

/// <summary>训练编排：导出数据集 -> 检测/搭建环境 -> 运行训练 -> 实时进度/日志（SignalR）。</summary>
public sealed class TrainingService
{
    private static readonly long StatusBroadcastIntervalMs = 250;
    private readonly IHubContext<TrainingHub> _hub;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<TrainingService> _logger;
    private readonly ConcurrentDictionary<string, TrainingStatus> _statuses = new();
    private readonly ConcurrentDictionary<string, Process> _processes = new();
    private readonly ConcurrentDictionary<string, CancellationTokenSource> _runCancellations = new();
    private readonly ConcurrentDictionary<string, long> _lastStatusBroadcastAt = new();
    private readonly object _statusFileLock = new();
    private int _running;

    /// <summary>共享训练环境 venv 目录（程序集目录下，跨工程共用，一个环境支持多个训练）。</summary>
    private static string VenvRoot => Path.Combine(AppContext.BaseDirectory, "train", ".env");

    /// <summary>训练状态持久化目录（程序集目录下，重启后恢复各项目的训练信息）。</summary>
    private static string StatusDir => Path.Combine(AppContext.BaseDirectory, "train", "statuses");

    public TrainingService(IHubContext<TrainingHub> hub, IServiceScopeFactory scopeFactory, ILogger<TrainingService> logger)
    {
        _hub = hub;
        _scopeFactory = scopeFactory;
        _logger = logger;
        LoadStatuses();
    }

    private void LoadStatuses()
    {
        try
        {
            var dir = StatusDir;
            if (!Directory.Exists(dir)) { return; }
            foreach (var f in Directory.EnumerateFiles(dir, "*.json"))
            {
                try
                {
                    var st = System.Text.Json.JsonSerializer.Deserialize<TrainingStatus>(File.ReadAllText(f));
                    if (st is null || string.IsNullOrEmpty(st.ProjectId)) { continue; }
                    // 重启后：上次运行中的训练视为已中断；完成的训练校验 best.pt 仍存在
                    if (st.IsActive) { st.Phase = TrainingPhase.Cancelled; st.Message = "上次训练已中断（应用重启）"; }
                    if (st.Phase == TrainingPhase.Complete && (string.IsNullOrEmpty(st.BestModelPath) || !File.Exists(st.BestModelPath)))
                    { st.Phase = TrainingPhase.Idle; st.BestModelPath = ""; st.Message = ""; }
                    st.LogTail.Clear();
                    _statuses[st.ProjectId] = st;
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
            var path = Path.Combine(StatusDir, SanitizeFileName(s.ProjectId) + ".json");
            var temporaryPath = path + ".tmp";
            lock (_statusFileLock)
            {
                Directory.CreateDirectory(StatusDir);
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

    public TrainingStatus? GetStatus(string projectId, bool includeLogs = true)
    {
        if (!_statuses.TryGetValue(projectId, out var status)) { return null; }
        lock (status) { return status.Clone(includeLogs); }
    }

    public bool IsActive(string projectId)
    {
        if (!_statuses.TryGetValue(projectId, out var status)) { return false; }
        lock (status) { return status.IsActive; }
    }

    public Task<TrainingStatus> StartAsync(string projectId, TrainingOptions options)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectId);
        ArgumentNullException.ThrowIfNull(options);
        if (Interlocked.CompareExchange(ref _running, 1, 0) != 0) { throw new InvalidOperationException("已有训练在运行，请等待完成或先停止。"); }

        var runOptions = CloneOptions(options);
        var status = new TrainingStatus { ProjectId = projectId, Phase = TrainingPhase.Preparing, TotalEpochs = runOptions.Epochs, ModelName = runOptions.Model, Message = "准备数据集…", UpdatedAt = DateTime.UtcNow };
        _statuses[projectId] = status;
        var cancellation = new CancellationTokenSource();
        if (!_runCancellations.TryAdd(projectId, cancellation))
        {
            cancellation.Dispose();
            Interlocked.Exchange(ref _running, 0);
            throw new InvalidOperationException("该项目的训练正在停止，请稍后重试。");
        }
        _ = RunPipelineAsync(projectId, runOptions, status, cancellation.Token);
        return Task.FromResult(status.Clone());
    }

    public async Task StopAsync(string projectId)
    {
        if (_runCancellations.TryGetValue(projectId, out var cancellation)) { await cancellation.CancelAsync(); }
        if (_processes.TryRemove(projectId, out var proc)) { try { proc.Kill(true); } catch { } }
        if (_statuses.TryGetValue(projectId, out var s))
        {
            lock (s) { if (s.IsActive) { s.Phase = TrainingPhase.Cancelled; s.Message = "已停止"; s.UpdatedAt = DateTime.UtcNow; } }
            await PushAsync(s, persist: true);
        }
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
    public async Task<(bool Ok, string Message)> ExportForValidationAsync(string projectId)
    {
        TrainingStatus? st;
        if (!_statuses.TryGetValue(projectId, out st) || st is null) { return (false, "没有训练状态，请先完成训练。"); }
        var best = st.BestModelPath;
        if (string.IsNullOrEmpty(best) || !File.Exists(best)) { return (false, "未找到训练产物 best.pt，请先完成训练。"); }

        var os = OperatingSystem.IsWindows() ? OsKind.Windows : OsKind.Linux;
        var venv = TrainEnvironmentPlanner.VenvYolo(VenvRoot, os);
        // ONNX 导出 opset：YOLOv26 系列要求 opset 18，其余（YOLOv5u~YOLOv12）沿用 opset 17
        var exportCmd = "export model=\"" + best + "\" format=onnx imgsz=640 opset=" + (st.ModelName.Contains("yolo26", StringComparison.OrdinalIgnoreCase) ? 18 : 17);
        Log(st, "$ " + venv + " " + exportCmd, "cmd", projectId);
        var (code, so, se) = await TrainingShell.RunAsync(venv, exportCmd);
        if (code != 0)
        {
            var err = LastNonEmpty(se, so);
            Log(st, "ONNX 导出失败：" + err, "err", projectId);
            return (false, "模型导出失败：" + err);
        }
        var onnxPath = Path.Combine(Path.GetDirectoryName(best)!, Path.GetFileNameWithoutExtension(best) + ".onnx");
        if (!File.Exists(onnxPath)) { return (false, "导出完成但未找到 onnx 文件：" + onnxPath); }

        var project = await LoadProjectAsync(projectId);
        using var scope = _scopeFactory.CreateScope();
        var valid = scope.ServiceProvider.GetRequiredService<ValidationService>();
        using var fs = File.OpenRead(onnxPath);
        var type = OnnxTypeOf(project?.LabelConfigXml);
        var r = await valid.AddModelAsync(fs, (project?.Name ?? "model") + "-best.onnx", "训练完成 " + st.ModelName + " · " + st.YoloVersion, type);
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

    private async Task<WorkspaceProject?> LoadProjectAsync(string projectId, CancellationToken cancellationToken = default)
    {
        using var scope = _scopeFactory.CreateScope();
        var workspaces = scope.ServiceProvider.GetRequiredService<WorkspaceService>();
        return await workspaces.GetProjectAsync(projectId, cancellationToken);
    }

    private async Task RunPipelineAsync(string projectId, TrainingOptions options, TrainingStatus status, CancellationToken cancellationToken)
    {
        try
        {
            var project = await LoadProjectAsync(projectId, cancellationToken);
            if (project is null) { await Fail(status, "工程不存在", projectId); return; }

            try
            {
                var cfg = LabelingConfigParser.Parse(project.LabelConfigXml);
                var ytask = YoloTaskRegistry.FromConfig(cfg);
                options.Task = YoloTaskRegistry.ToCommand(ytask);
                options.Model = YoloTaskRegistry.ModelFor(ytask, options.Model);
            }
            catch { }

            Set(status, TrainingPhase.Preparing, "导出 YOLO 数据集…");
            var envRoot = Path.Combine(AppContext.BaseDirectory, "train");
            Directory.CreateDirectory(envRoot);
            var projectDir = Path.Combine(envRoot, SanitizeName(project.Name));
            var dataYaml = WriteDataset(project, projectDir, options, cancellationToken);

            Set(status, TrainingPhase.EnvironmentCheck, "检测训练环境…");
            var snap = await DetectEnvironmentAsync(cancellationToken);
            var plan = TrainEnvironmentPlanner.Plan(snap);

            if (!plan.EnvReady)
            {
                Set(status, TrainingPhase.Installing, "搭建训练环境…");
                foreach (var step in plan.Steps)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    Set(status, TrainingPhase.Installing, step.Description);
                    Log(status, "$ " + step.Command.Executable + " " + step.Command.Arguments, "cmd", projectId);
                    if (string.IsNullOrEmpty(step.Command.Executable)) { await Fail(status, step.Description, projectId); return; }
                    var (code, so, se) = await TrainingShell.RunAsync(step.Command.Executable, step.Command.Arguments, cancellationToken);
                    if (so.Length > 0) Log(status, so, "out", projectId);
                    if (se.Length > 0) Log(status, se, "err", projectId);
                    if (code != 0) { await Fail(status, "环境搭建失败（退出码 " + code + "）：" + LastNonEmpty(se, so), projectId); return; }
                }
            }

            var (cuda, yoloVer) = await ResolveRuntimeAsync(plan, cancellationToken);
            var useGpu = plan.UseGpu && cuda;
            var device = useGpu ? "0" : "cpu";
            if (!useGpu) { var warn = "当前走 CPU 训练，速度较慢、效率较低。"; Set(status, TrainingPhase.Training, warn); Log(status, warn, "warn", projectId); }
            options.Device = device;

            Set(status, TrainingPhase.Training, "开始训练…");
            status.YoloVersion = yoloVer; status.Device = device; status.GpuName = snap.Gpu?.Name ?? "";
            var trainCmd = YoloCommandBuilder.BuildTrain(plan.VenvYolo, dataYaml, options);
            Log(status, "$ " + trainCmd, "cmd", projectId);

            var exit = await RunTrainProcessAsync(projectId, status, trainCmd, projectDir, cancellationToken);
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
            _processes.TryRemove(projectId, out _);
            if (_runCancellations.TryRemove(projectId, out var cancellation)) { cancellation.Dispose(); }
            _lastStatusBroadcastAt.TryRemove(projectId, out _);
            Interlocked.Exchange(ref _running, 0);
        }
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
    private string WriteDataset(WorkspaceProject project, string projectDir, TrainingOptions options, CancellationToken cancellationToken)
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

        var uploads = Path.Combine(AppContext.BaseDirectory,"wwwroot", "data", "uploads", project.Id);
        var tasks = project.Tasks.Where(x => !string.IsNullOrEmpty(x.Data?["image"]?.ToString())).ToList();
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
            foreach (var task in tasks)
            {
                cancellationToken.ThrowIfCancellationRequested();
                cid++;
                var cls = ClassifyOf(task);
                if (string.IsNullOrEmpty(cls)) { continue; }
                var toVal = useVal && cid <= valCount;
                var root = toVal ? valRoot : trainRoot;
                var clsDir = Path.Combine(root, SanitizeName(cls));
                Directory.CreateDirectory(clsDir);
                var imgName = ExportService.ResolveFileName(task.Data!["image"]!.ToString());
                var srcImg = Path.Combine(uploads, imgName);
                if (File.Exists(srcImg)) { File.Copy(srcImg, Path.Combine(clsDir, cid + Path.GetExtension(imgName)), true); }
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
            if (File.Exists(src)) File.Copy(src, Path.Combine(imgTargetDir, id + ext), true);
            var labelText = YoloLabelExporter.Build(task, taskType, classes);
            if (!string.IsNullOrWhiteSpace(labelText)) { File.WriteAllText(Path.Combine(lblTargetDir, id + ".txt"), labelText); }
        }

        File.WriteAllText(Path.Combine(projectDir, "classes.txt"), string.Join("\n", classes.Select((c, idx) => idx + " " + c)) + "\n");

        var totalLabels = Directory.EnumerateFiles(labelsDir, "*.txt").Count() + (valLabelsDir is not null && Directory.Exists(valLabelsDir) ? Directory.EnumerateFiles(valLabelsDir, "*.txt").Count() : 0);
        if (totalLabels == 0)
        {
            throw new InvalidOperationException("导出数据集中没有任何标注（图片数 " + tasks.Count + "），请先在标注器里为图片添加标注后再训练。");
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

    private async Task<TrainingEnvSnapshot> DetectEnvironmentAsync(CancellationToken cancellationToken)
    {
        var os = OperatingSystem.IsWindows() ? OsKind.Windows : OperatingSystem.IsLinux() ? OsKind.Linux : OsKind.Mac;
        var python = os == OsKind.Windows ? "python" : "python3";
        var snap = new TrainingEnvSnapshot { Os = os, PythonCmd = python };
        snap.HasPython = (await TryRun(python, "--version", cancellationToken)).Item1 == 0;
        snap.HasPip = snap.HasPython && (await TryRun(python, "-m pip --version", cancellationToken)).Item1 == 0;
        var (gpuCode, gpuOut, _) = await TrainingShell.RunAsync("nvidia-smi", "--query-gpu=name,compute_cap,driver_version,memory.total --format=csv", cancellationToken);
        if (gpuCode == 0) { var gpus = NvidiaSmiParser.ParseCsv(gpuOut); snap.Gpu = gpus.FirstOrDefault(); }

        var venvRoot = VenvRoot;
        snap.VenvPath = venvRoot;
        var venvPython = TrainEnvironmentPlanner.VenvPython(venvRoot, os);
        snap.VenvExists = File.Exists(venvPython);
        if (snap.VenvExists)
        {
            snap.VenvHasTorch = (await TryRun(venvPython, "-m pip show torch", cancellationToken)).Item1 == 0;
            snap.VenvHasUltralytics = (await TryRun(venvPython, "-m pip show ultralytics", cancellationToken)).Item1 == 0;
        }
        snap.TorchInstalled = snap.VenvHasTorch; snap.UltralyticsInstalled = snap.VenvHasUltralytics;
        return snap;
    }

    private async Task<(bool Cuda, string YoloVersion)> ResolveRuntimeAsync(SetupPlan plan, CancellationToken cancellationToken)
    {
        if (!File.Exists(plan.VenvPython)) return (false, "未知");
        var (code, out_, err) = await TrainingShell.RunAsync(plan.VenvPython, "-c \"import torch, ultralytics; print(torch.cuda.is_available()); print(ultralytics.__version__)\"", cancellationToken);
        if (code != 0) return (false, "未安装");
        var lines = (out_ + "\n" + err).Split('\n', StringSplitOptions.RemoveEmptyEntries);
        var cuda = lines.Any(l => l.Trim().Equals("True", StringComparison.OrdinalIgnoreCase));
        var ver = lines.Select(l => l.Trim()).FirstOrDefault(l => System.Text.RegularExpressions.Regex.IsMatch(l, @"^\d+\.\d+\.\d+$")) ?? "未知";
        return (cuda, ver);
    }

    private async Task<int> RunTrainProcessAsync(string projectId, TrainingStatus status, string command, string workDir, CancellationToken cancellationToken)
    {
        var (file, args) = ParseCommandLine(command);
        var psi = new ProcessStartInfo(file, args) { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, WorkingDirectory = workDir, StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8 };
        using var proc = Process.Start(psi);
        if (proc is null) { await Fail(status, "无法启动训练进程", projectId); return -1; }
        _processes[projectId] = proc;
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
        finally { _processes.TryRemove(projectId, out _); }
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
        _ = PushLogSafelyAsync(projectId, text, level);
    }

    private void QueueStatusBroadcast(TrainingStatus status)
    {
        var now = Environment.TickCount64;
        while (true)
        {
            var previous = _lastStatusBroadcastAt.GetOrAdd(status.ProjectId, 0);
            if (now - previous < StatusBroadcastIntervalMs) { return; }
            if (_lastStatusBroadcastAt.TryUpdate(status.ProjectId, now, previous)) { break; }
        }
        _ = PushSafelyAsync(status, persist: false);
    }

    private async Task PushSafelyAsync(TrainingStatus status, bool persist)
    {
        try { await PushAsync(status, persist); }
        catch (Exception error) { _logger.LogWarning(error, "无法推送项目 {ProjectId} 的训练状态", status.ProjectId); }
    }

    private async Task PushLogSafelyAsync(string projectId, string text, string level)
    {
        try { await TrainingHub.PushLog(_hub, projectId, text, level); }
        catch (Exception error) { _logger.LogWarning(error, "无法推送项目 {ProjectId} 的训练日志", projectId); }
    }

    private Task PushAsync(TrainingStatus status, bool persist)
    {
        if (persist) { SaveStatus(status); }
        TrainingStatus snapshot;
        lock (status) { snapshot = status.Clone(includeLogs: false); }
        return TrainingHub.PushStatus(_hub, status.ProjectId, snapshot);
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

    private static async Task<(int, string, string)> TryRun(string file, string args, CancellationToken cancellationToken) { try { return await TrainingShell.RunAsync(file, args, cancellationToken); } catch (OperationCanceledException) { throw; } catch { return (-1, string.Empty, string.Empty); } }
    private static string LastNonEmpty(string a, string b) => (!string.IsNullOrWhiteSpace(a) ? a.Trim().Split('\n').LastOrDefault() : null) ?? (!string.IsNullOrWhiteSpace(b) ? b.Trim().Split('\n').LastOrDefault() : null) ?? "未知";
    private static string SanitizeName(string name) { var bad = Path.GetInvalidFileNameChars(); var s = new string(name.Select(c => bad.Contains(c) ? '_' : c).ToArray()).Trim(); return string.IsNullOrEmpty(s) ? "project" : s; }
    private static (string File, string Args) ParseCommandLine(string command)
    {
        var q = command.IndexOf('\"');
        if (q < 0) { var sp = command.IndexOf(' '); return sp < 0 ? (command, string.Empty) : (command[..sp], command[(sp + 1)..]); }
        var end = command.IndexOf('\"', q + 1);
        var file = command[(q + 1)..end];
        var args = command[(end + 1)..].Trim();
        if (args.StartsWith('\"')) { var e2 = args.IndexOf('\"', 1); file = args[1..e2]; args = args[(e2 + 1)..].Trim(); }
        return (file, args);
    }
}
