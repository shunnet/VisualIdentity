using Snet.Yolo.Server.models;
using Snet.Yolo.Tasks.Core.Anomalib;
using Snet.Yolo.Tasks.Core.Training;

namespace Snet.Yolo.Tasks.Services;

/// <summary>环境重置的只读快照；独立于训练状态，不覆盖已有指标和模型信息。</summary>
/// <param name="IsActive">后台重装是否仍在执行。</param>
/// <param name="Succeeded">安装和运行时校验是否全部通过。</param>
/// <param name="Message">最近一条进度或结果说明。</param>
/// <param name="Logs">安装日志的独立副本。</param>
public sealed record TrainingEnvironmentResetStatus(bool IsActive, bool Succeeded, string Message, IReadOnlyList<string> Logs);

/// <summary>重建指定算法的共享环境；调用方必须先取得该算法的训练互斥权。</summary>
/// <param name="owner">发起重置的用户。</param>
/// <param name="projectId">发起重置的项目。</param>
public sealed class TrainingEnvironmentResetOperation(string owner, string projectId)
{
    private readonly object _sync = new();
    private readonly Queue<string> _logs = new();
    private bool _active = true, _succeeded;
    private string _message = "正在准备重置训练环境…";

    /// <summary>发起操作的用户，用于隔离重置日志。</summary>
    public string Owner { get; } = owner;
    /// <summary>发起操作的项目。</summary>
    public string ProjectId { get; } = projectId;
    /// <summary>环境是否仍在重建。</summary>
    public bool IsActive { get { lock (_sync) { return _active; } } }
    /// <summary>读取独立副本，避免后台日志更新与页面枚举冲突。</summary>
    public TrainingEnvironmentResetStatus Snapshot()
    { lock (_sync) { return new(_active, _succeeded, _message, _logs.ToArray()); } }

    private void Log(string message)
    {
        lock (_sync)
        {
            _message = message;
            _logs.Enqueue($"[{DateTime.Now:HH:mm:ss}] {message}");
            while (_logs.Count > 500) { _logs.Dequeue(); }
        }
    }

    /// <summary>后台执行重装；取消和失败均记录结果并释放活动状态。</summary>
    /// <param name="kind">YOLO 或 Anomalib 项目类型。</param>
    /// <param name="device">页面当前设备选择。</param>
    /// <param name="proxy">仅传给安装子进程的代理。</param>
    /// <param name="caBundle">仅传给安装子进程的 CA。</param>
    /// <param name="token">宿主停止时取消整个安装进程树。</param>
    internal async Task RunAsync(ProjectKind kind, string device, string? proxy, string? caBundle, CancellationToken token)
    {
        try
        {
            token.ThrowIfCancellationRequested();
            var os = OperatingSystem.IsWindows() ? OsKind.Windows : OperatingSystem.IsMacOS() ? OsKind.Mac : OsKind.Linux;
            PythonLauncher? launcher = null;
            foreach (var probe in PythonDiscovery.VersionProbes(os))
            {
                var result = await TrainingShell.RunAsync(probe.Launcher.Executable, probe.Arguments, TimeSpan.FromSeconds(20), token);
                if (result.ExitCode == 0 && PythonDiscovery.IsPython3(result.Stdout, result.Stderr)) { launcher = probe.Launcher; break; }
            }
            if (launcher is null) { throw new InvalidOperationException(PythonDiscovery.MissingPythonMessage(os)); }
            // 先确认 ensurepip 可用，再移除旧环境，避免 Debian/Ubuntu 缺少 venv 支持时提前破坏环境。
            var venvProbe = await TrainingShell.RunAsync(launcher.Executable, launcher.WithArguments(["-c", "import venv, ensurepip"]), TimeSpan.FromSeconds(20), token);
            if (venvProbe.ExitCode != 0) { throw new InvalidOperationException(PythonDiscovery.MissingVenvMessage(os) + " " + venvProbe.Stderr); }
            var selected = GpuDeviceSelection.Parse(device, anomalib: kind == ProjectKind.Anomalib);
            GpuInfo? gpu = null;
            if (device != "cpu" && os != OsKind.Mac)
            {
                foreach (var executable in NvidiaSmi.CandidateExecutables(os))
                {
                    foreach (var query in new[] { NvidiaSmi.RichQuery, NvidiaSmi.LegacyQuery })
                    {
                        var result = await TrainingShell.RunAsync(executable, query, TimeSpan.FromSeconds(10), token);
                        var gpus = result.ExitCode == 0 ? NvidiaSmiParser.ParseCsv(result.Stdout) : [];
                        if (gpus.Count == 0) { continue; }
                        if (selected.Any(index => index >= gpus.Count)) { throw new InvalidOperationException("所选 GPU 不存在，请重新检测设备。"); }
                        var index = selected.Length == 0 ? 0 : selected.MinBy(index => gpus[index].ComputeCapParsed ?? 0);
                        gpu = gpus[index]; break;
                    }
                    if (gpu is not null) { break; }
                }
            }
            var channel = gpu is null ? "cpu" : CudaMapping.Select(gpu.ComputeCapParsed, gpu.DriverVersion, os);
            if (selected.Length > 0 && channel == "cpu") { throw new InvalidOperationException("所选 GPU 不可用，请更新驱动或在训练配置中选择 CPU 后重试。"); }
            var root = AppContext.BaseDirectory;
            var algorithm = kind == ProjectKind.Anomalib ? "anomalib" : "yolo";
            var venv = Path.GetFullPath(Path.Combine(root, "train", algorithm, ".env"));
            var python = TrainEnvironmentPlanner.VenvPython(venv, os);
            Log($"重装 {algorithm} 环境：{venv}；PyTorch 通道 {channel}。项目数据、权重和训练产物保留。");
            foreach (var step in CreatePlan(kind, root, launcher, os, gpu))
            {
                token.ThrowIfCancellationRequested();
                Log(step.Description);
                if (step.Kind == SetupStepKind.RecreateVenv && Directory.Exists(venv)) { VenvRebuilder.Reset(venv); }
                for (var attempt = 1; attempt <= (step.IsNetwork ? PipProxyPolicy.MaxAttempts : 1); attempt++)
                {
                    var arguments = step.IsNetwork ? PipProxyPolicy.BuildArguments(step.Command.ArgumentList, attempt, proxy) : step.Command.ArgumentList;
                    var result = await TrainingShell.RunStreamingAsync(step.Command.Executable, arguments, root,
                        PipProxyPolicy.BuildEnvironmentOverrides(attempt, caBundle), Log, TimeSpan.FromMinutes(15), token);
                    if (result.ExitCode == 0) { break; }
                    if (!step.IsNetwork || attempt == PipProxyPolicy.MaxAttempts) { throw new InvalidOperationException(step.Description + "失败：" + result.Tail); }
                    Log("下载失败，正在按代理重试策略重新安装…");
                }
            }
            var script = kind == ProjectKind.Anomalib ? AnomalibEnvironmentPlanner.EnvironmentProbeScript
                : TorchRuntimeProbe.EnvironmentScript + "; import ultralytics; print(ultralytics.__version__)";
            // 仅作用于校验子进程，避免首次导入 Ultralytics 时写入操作系统用户的全局配置。
            script = "import os; os.environ['YOLO_CONFIG_DIR'] = "
                + System.Text.Json.JsonSerializer.Serialize(Path.Combine(venv, ".ultralytics")) + "; " + script;
            // 除检测 CUDA 可见性外，执行一个真实小张量操作，确认所选 GPU 的内核可运行。
            if (channel != "cpu")
            { script += "; [torch.ones(1, device='cuda:' + str(i)).sum().item() for i in [" + string.Join(',', selected.Length == 0 ? [0] : selected) + "]]"; }
            var verification = await TrainingShell.RunAsync(python, ["-c", script], TimeSpan.FromMinutes(2), token);
            Log(verification.Stdout);
            if (verification.ExitCode != 0 || kind == ProjectKind.Anomalib && !AnomalibEnvironmentPlanner.IsEnvironmentReady(verification.Stdout, channel))
            { throw new InvalidOperationException("环境安装后校验失败：" + verification.Stderr); }
            lock (_sync) { _succeeded = true; }
            Log("环境重置完成，可以开始训练。");
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { Log("环境重置已取消。"); }
        catch (Exception error) { Log("环境重置失败：" + error.Message); }
        finally { lock (_sync) { _active = false; } }
    }

    /// <summary>生成仅包含环境安装的计划，不包含数据集导出、训练或模型注册。</summary>
    internal static IReadOnlyList<SetupStep> CreatePlan(ProjectKind kind, string root, PythonLauncher launcher, OsKind os, GpuInfo? gpu)
    {
        var channel = gpu is null ? "cpu" : CudaMapping.Select(gpu.ComputeCapParsed, gpu.DriverVersion, os);
        if (kind == ProjectKind.Anomalib) { return AnomalibEnvironmentPlanner.Create(root, launcher, os, channel).Steps; }
        var plan = TrainEnvironmentPlanner.Plan(new TrainingEnvSnapshot
        {
            Os = os, HasPython = true, HasVenv = true, PythonCmd = launcher.Executable,
            PythonArguments = launcher.PrefixArguments, Gpu = gpu, VenvPath = Path.Combine(root, "train", "yolo", ".env"),
        });
        return plan.Steps.Select(step => step.Kind == SetupStepKind.PipInstallTorch
            ? step with { Command = TrainEnvironmentPlanner.TorchInstallCommand(os, plan.UseGpu, channel, plan.VenvPython, latestCompatible: true)! }
            : step).ToArray();
    }
}
