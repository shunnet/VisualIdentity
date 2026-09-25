namespace Snet.Yolo.Tasks.Services;

using System.Collections.Concurrent;
using Snet.Yolo.Server.models;
using Snet.Yolo.Server.Anomalib;
using Snet.Yolo.Tasks.Core.Anomalib;
using Snet.Yolo.Tasks.Core.Training;

/// <summary>把 Anomalib 环境准备、训练和模型注册组织为可跨页面观察的后台任务。</summary>
public sealed class AnomalibWorkflowService(IServiceScopeFactory scopeFactory, AnomalibTrainingService training)
{
    /// <summary>当前进程内按用户和工程隔离的训练任务。</summary>
    private readonly ConcurrentDictionary<string, RunState> _runs = new(StringComparer.Ordinal);

    /// <summary>防止两个工程同时修改共用的 Anomalib 虚拟环境。</summary>
    private readonly SemaphoreSlim _environmentLock = new(1, 1);

    /// <summary>串行使用共享虚拟环境，避免重建环境时影响另一个正在运行的训练进程。</summary>
    private readonly SemaphoreSlim _trainingLock = new(1, 1);

    /// <summary>开始训练并立即返回；同一个工程已经运行时返回 false。</summary>
    public async Task<bool> StartAsync(string owner, string projectId, AnomalibTrainingOptions options, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(owner);
        ArgumentException.ThrowIfNullOrWhiteSpace(projectId);
        ArgumentNullException.ThrowIfNull(options);
        options.Validate();
        using var scope = scopeFactory.CreateScope();
        var project = await scope.ServiceProvider.GetRequiredService<WorkspaceService>().GetProjectForOwnerAsync(owner, projectId, cancellationToken);
        if (project?.Kind != ProjectKind.Anomalib) { throw new InvalidOperationException("Anomalib 工程不存在或无权访问。"); }
        var paths = ResolveProjectImages(owner, projectId, project.Tasks.Select(task => task.Data?["image"]?.ToString()));
        if (paths.Count < 10) { throw new InvalidOperationException("请先上传至少 10 张不同的正常图片。"); }
        var key = Key(owner, projectId);
        var state = new RunState(owner, projectId, options);
        while (true)
        {
            if (_runs.TryGetValue(key, out var previous))
            {
                if (previous.Active) { return false; }
                if (_runs.TryUpdate(key, state, previous)) { break; }
            }
            else if (_runs.TryAdd(key, state)) { break; }
        }
        _ = Task.Run(() => RunAsync(state, paths, options), CancellationToken.None);
        return true;
    }

    /// <summary>读取一个工程最后一次训练的独立状态快照。</summary>
    public AnomalibTrainingStatus? GetStatus(string owner, string projectId)
        => _runs.TryGetValue(Key(owner, projectId), out var state) ? state.Snapshot() : null;

    /// <summary>要求正在运行的工程训练停止。</summary>
    public void Stop(string owner, string projectId)
    {
        if (_runs.TryGetValue(Key(owner, projectId), out var state) && state.Active) { state.RequestStop(); }
    }

    /// <summary>取消训练并等待后台进程退出，供删除工程前安全回收文件使用。</summary>
    public async Task StopAndWaitAsync(string owner, string projectId, CancellationToken cancellationToken = default)
    {
        if (!_runs.TryGetValue(Key(owner, projectId), out var state) || !state.Active) { return; }
        state.RequestStop();
        await state.Completion.Task.WaitAsync(cancellationToken);
    }

    /// <summary>执行后台流程并保证最终状态总能被页面读取。</summary>
    private async Task RunAsync(RunState state, IReadOnlyList<string> images, AnomalibTrainingOptions options)
    {
        try
        {
            state.Update(AnomalibTrainingPhase.PreparingEnvironment, "正在等待 Anomalib 训练资源…");
            await _trainingLock.WaitAsync(state.Cancellation.Token);
            try
            {
                state.Update(AnomalibTrainingPhase.PreparingEnvironment, "正在检查独立 Anomalib 训练环境…");
                var python = await EnsureEnvironmentAsync(state, options.Device);
                var request = new AnomalibTrainingRequest
                {
                    Owner = state.Owner,
                    ProjectId = state.ProjectId,
                    WorkingDirectory = AnomalibModelRegistry.ProjectRoot(state.Owner, state.ProjectId),
                    PythonExecutable = python,
                    Images = images,
                    Options = options,
                };
                var result = await training.TrainAsync(request, state.SetStatus, state.Cancellation.Token);
                if (!result.Succeeded) { state.Update(AnomalibTrainingPhase.Failed, result.Message); }
            }
            finally { _trainingLock.Release(); }
        }
        catch (OperationCanceledException) when (state.Cancellation.IsCancellationRequested)
        {
            state.Update(AnomalibTrainingPhase.Cancelled, "训练已取消。");
        }
        catch (Exception error)
        {
            state.Update(AnomalibTrainingPhase.Failed, "Anomalib 训练失败：" + error.Message);
        }
        finally
        {
            state.Active = false;
            state.Cancellation.Dispose();
            state.Completion.TrySetResult();
        }
    }

    /// <summary>检查固定版本的虚拟环境，必要时创建并安装 PyTorch、Anomalib 和 ONNX 依赖。</summary>
    private async Task<string> EnsureEnvironmentAsync(RunState state, string requestedDevice)
    {
        await _environmentLock.WaitAsync(state.Cancellation.Token);
        try
        {
            var os = OperatingSystem.IsWindows() ? OsKind.Windows : OperatingSystem.IsMacOS() ? OsKind.Mac : OsKind.Linux;
            var venv = Path.Combine(AppContext.BaseDirectory, "train", "anomalib", ".env");
            var venvPython = os == OsKind.Windows ? Path.Combine(venv, "Scripts", "python.exe") : Path.Combine(venv, "bin", "python");
            var channel = await DetectTorchChannelAsync(os, state.Cancellation.Token);
            if (requestedDevice == "cuda" && channel == "cpu")
            {
                throw new InvalidOperationException("未检测到可用于 Anomalib 训练的 NVIDIA CUDA 环境；请选择 CPU 或自动设备。");
            }
            if (File.Exists(venvPython))
            {
                var probe = await TrainingShell.RunAsync(venvPython, ["-c", "import importlib.metadata as m; import torch; print(m.version('anomalib')); print(torch.cuda.is_available()); print(torch.version.cuda or 'cpu')"], TimeSpan.FromMinutes(2), state.Cancellation.Token);
                var lines = probe.Stdout.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries);
                if (probe.ExitCode == 0 && lines.Length >= 3 && lines[0].Trim() == AnomalibEnvironmentPlanner.AnomalibVersion
                    && (channel == "cpu" || lines[1].Trim().Equals("True", StringComparison.OrdinalIgnoreCase)
                        && CudaMapping.RuntimeMatches(channel, lines[2].Trim()))) { return venvPython; }
            }
            var launcher = await FindPythonAsync(os, state.Cancellation.Token)
                ?? throw new InvalidOperationException(PythonDiscovery.MissingPythonMessage(os));
            var venvProbe = await TrainingShell.RunAsync(launcher.Executable, launcher.WithArguments(PythonDiscovery.VenvProbeArguments), TimeSpan.FromSeconds(20), state.Cancellation.Token);
            if (venvProbe.ExitCode != 0) { throw new InvalidOperationException(PythonDiscovery.MissingVenvMessage(os)); }
            var plan = AnomalibEnvironmentPlanner.Create(AppContext.BaseDirectory, launcher, os, channel);
            foreach (var step in plan.Steps)
            {
                state.Update(AnomalibTrainingPhase.PreparingEnvironment, step.Description + "…");
                if (step.Kind == SetupStepKind.RecreateVenv && Directory.Exists(plan.VenvDirectory))
                {
                    var expected = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "train", "anomalib", ".env"));
                    if (!string.Equals(Path.GetFullPath(plan.VenvDirectory), expected, PathComparison)) { throw new InvalidOperationException("虚拟环境路径校验失败。"); }
                    VenvRebuilder.Reset(plan.VenvDirectory);
                }
                var outcome = await TrainingShell.RunStreamingAsync(step.Command.Executable, step.Command.ArgumentList, AppContext.BaseDirectory,
                    null, state.AppendLog, TimeSpan.FromMinutes(15), state.Cancellation.Token);
                if (outcome.ExitCode != 0) { throw new InvalidOperationException(step.Description + "失败：" + outcome.Tail); }
            }
            return plan.VenvPython;
        }
        finally { _environmentLock.Release(); }
    }

    /// <summary>按平台探测一个真正可执行的 Python 3 启动器。</summary>
    private static async Task<PythonLauncher?> FindPythonAsync(OsKind os, CancellationToken cancellationToken)
    {
        foreach (var probe in PythonDiscovery.VersionProbes(os))
        {
            var result = await TrainingShell.RunAsync(probe.Launcher.Executable, probe.Arguments, TimeSpan.FromSeconds(15), cancellationToken);
            if (result.ExitCode == 0 && PythonDiscovery.IsPython3(result.Stdout, result.Stderr)) { return probe.Launcher; }
        }
        return null;
    }

    /// <summary>根据显卡计算能力和驱动选择 PyTorch 官方 CUDA 通道。</summary>
    private static async Task<string> DetectTorchChannelAsync(OsKind os, CancellationToken cancellationToken)
    {
        if (os == OsKind.Mac) { return "cpu"; }
        foreach (var executable in NvidiaSmi.CandidateExecutables(os))
        {
            foreach (var query in new[] { NvidiaSmi.RichQuery, NvidiaSmi.LegacyQuery })
            {
                var result = await TrainingShell.RunAsync(executable, query, TimeSpan.FromSeconds(8), cancellationToken);
                if (result.ExitCode != 0) { continue; }
                var gpu = NvidiaSmiParser.ParseCsv(result.Stdout).FirstOrDefault(candidate => candidate.HasGpu);
                if (gpu is not null) { return CudaMapping.Select(CudaMapping.TryParse(gpu.ComputeCap), gpu.DriverVersion, os); }
            }
        }
        return "cpu";
    }

    /// <summary>把数据库中的受保护图片 URL 映射到所属用户和工程的物理文件。</summary>
    private static IReadOnlyList<string> ResolveProjectImages(string owner, string projectId, IEnumerable<string?> imageUrls)
    {
        var ownerSegment = UserStoragePath.Segment(owner);
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "wwwroot", "data", "uploads", ownerSegment, projectId));
        var prefix = $"/uploads/{ownerSegment}/{projectId}/";
        var paths = new List<string>();
        foreach (var url in imageUrls)
        {
            if (url is null || !url.StartsWith(prefix, StringComparison.Ordinal)) { continue; }
            var name = Uri.UnescapeDataString(url[prefix.Length..]);
            if (name != Path.GetFileName(name) || name is "." or "..") { continue; }
            var path = Path.GetFullPath(Path.Combine(root, name));
            if (path.StartsWith(root + Path.DirectorySeparatorChar, PathComparison) && File.Exists(path)) { paths.Add(path); }
        }
        return paths;
    }

    /// <summary>合成用户与工程隔离的任务键。</summary>
    private static string Key(string owner, string projectId) => owner + "\n" + projectId;

    /// <summary>按当前平台比较磁盘路径。</summary>
    private static StringComparison PathComparison => OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

    /// <summary>保存后台任务状态、取消令牌和线程安全快照。</summary>
    private sealed class RunState(string owner, string projectId, AnomalibTrainingOptions options)
    {
        /// <summary>保护可变状态的同步锁。</summary>
        private readonly object _sync = new();

        /// <summary>最后一次完整的训练状态。</summary>
        private AnomalibTrainingStatus _status = new() { Owner = owner, ProjectId = projectId, Model = options.Model, Device = options.Device, ImageSize = options.ImageSize, MaxEpochs = options.MaxEpochs, Phase = AnomalibTrainingPhase.PreparingEnvironment, Message = "正在准备训练…" };

        /// <summary>工程所属用户名。</summary>
        public string Owner { get; } = owner;

        /// <summary>工程标识。</summary>
        public string ProjectId { get; } = projectId;

        /// <summary>整个训练流程的取消令牌源。</summary>
        public CancellationTokenSource Cancellation { get; } = new();

        /// <summary>后台训练退出时完成，供删除工程安全等待。</summary>
        public TaskCompletionSource Completion { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        /// <summary>后台流程是否仍在运行。</summary>
        public volatile bool Active = true;

        /// <summary>复制最后的训练状态，避免页面读取到半更新的数据。</summary>
        public AnomalibTrainingStatus Snapshot() { lock (_sync) { return _status.Clone(); } }

        /// <summary>接受训练内核产生的状态快照。</summary>
        public void SetStatus(AnomalibTrainingStatus status) { lock (_sync) { _status = status.Clone(); } }

        /// <summary>请求取消训练；与训练线程收尾并发时忽略已释放的取消源。</summary>
        public void RequestStop()
        {
            try { Cancellation.Cancel(); }
            catch (ObjectDisposedException) { }
        }

        /// <summary>保留最近 120 行环境安装输出，供页面显示真实进度。</summary>
        public void AppendLog(string line)
        {
            lock (_sync)
            {
                var lines = _status.LogTail.ToList();
                lines.Add(line);
                if (lines.Count > 120) { lines.RemoveRange(0, lines.Count - 120); }
                _status.LogTail = lines;
                _status.UpdatedAt = DateTime.UtcNow;
            }
        }

        /// <summary>更新环境、失败或取消阶段的状态。</summary>
        public void Update(AnomalibTrainingPhase phase, string message)
        {
            lock (_sync)
            {
                _status.Phase = phase;
                _status.Message = message;
                if (phase == AnomalibTrainingPhase.Failed) { _status.LastError = message; }
                _status.UpdatedAt = DateTime.UtcNow;
            }
        }
    }
}
