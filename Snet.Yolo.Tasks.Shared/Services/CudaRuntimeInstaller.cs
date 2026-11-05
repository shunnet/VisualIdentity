namespace Snet.Yolo.Tasks.Services;

using Microsoft.Extensions.Configuration;
using Snet.Yolo.Tasks.Core.Training;

/// <summary>
/// 为 ONNX Runtime 1.31.1 的 CUDA 13 执行提供程序准备用户态运行库。
/// 只安装到应用目录，不修改系统驱动、PATH 或 LD_LIBRARY_PATH，也不要求管理员权限。
/// </summary>
public sealed class CudaRuntimeInstaller
{
    private static readonly TimeSpan ProbeTimeout = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan InstallStallTimeout = TimeSpan.FromMinutes(5);
    private static readonly IReadOnlyList<string> RuntimePackages = new[]
    {
        // CUDA 13.0.3 官方组件组合；固定版本，防止重新部署时解析到更高驱动要求的组件。
        "nvidia-cuda-runtime==13.0.96",
        "nvidia-cublas==13.1.1.3",
        "nvidia-cufft==12.0.0.61",
        "nvidia-curand==10.4.0.35",
        "nvidia-cusolver==12.0.4.66",
        "nvidia-cusparse==12.6.3.3",
        "nvidia-cuda-nvrtc==13.0.88",
        "nvidia-nvjitlink==13.0.88",
        "nvidia-cudnn-cu13==9.19.0.56",
    };

    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly ILogger<CudaRuntimeInstaller> _logger;
    private readonly string? _proxy;
    private readonly string? _caBundle;

    /// <summary>创建 CUDA 运行库安装器。</summary>
    public CudaRuntimeInstaller(ILogger<CudaRuntimeInstaller> logger, IConfiguration? configuration = null)
    {
        _logger = logger;
        _proxy = TrainingService.ReadProxy(configuration);
        _caBundle = TrainingService.ReadCaBundle(configuration);
    }

    /// <summary>应用私有 CUDA 运行库目录。</summary>
    public static string InstallDirectory => Path.Combine(AppContext.BaseDirectory, "train", "cuda-runtime", "cu13");

    /// <summary>NVIDIA 官方 CUDA 13 运行库 wheel 列表。</summary>
    public static IReadOnlyList<string> Packages => RuntimePackages;

    /// <summary>检测 GPU/驱动并在缺少运行库时自动下载、安装，然后重新验证。</summary>
    /// <param name="progress">面向用户的准备进度回调。</param>
    /// <param name="cancellationToken">取消探测、下载和安装。</param>
    /// <param name="gpuId">实际需要使用的 GPU 编号。</param>
    public async Task<HardwarePreparationResult> EnsureAsync(Action<string>? progress, CancellationToken cancellationToken, int gpuId = 0)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(gpuId);
        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (OperatingSystem.IsMacOS())
            {
                return new(true, false, false, "macOS 不支持 NVIDIA CUDA 推理，请选择 CPU；当前产品未集成 CoreML/MPS。");
            }

            progress?.Invoke("正在检测 NVIDIA GPU 与驱动……");
            var os = OperatingSystem.IsWindows() ? OsKind.Windows : OsKind.Linux;
            var gpu = await DetectGpuAsync(os, gpuId, cancellationToken);
            if (gpu is null)
            {
                return new(true, false, false, $"未找到可用的 GPU {gpuId}。" + MissingDriverMessage(os, IsWsl()));
            }

            if (gpu.ComputeCapParsed is < 7.5)
            {
                return new(true, false, false, $"检测到 {gpu.Name}（计算能力 {gpu.ComputeCap}），当前 CUDA 13 推理包不支持该架构；请使用 CPU 推理或适配旧卡的独立构建。");
            }
            if (!CudaMapping.DriverSupports("cu130", gpu.DriverVersion, os))
            {
                var minimum = os == OsKind.Windows ? "580.88" : "580.65.06";
                return new(true, false, false,
                    $"检测到 {gpu.Name}，但驱动 {gpu.DriverVersion} 无法确认满足 CUDA 13 所需的最低版本 {minimum}。" + DriverUpdateMessage(os, IsWsl()));
            }

            var current = CudaRuntimeLibraries.TryPrepare(AppContext.BaseDirectory, message => _logger.LogInformation("{Message}", message));
            if (current is null)
            {
                return new(true, true, false, ReadyMessage(gpu, os, installed: false));
            }

            progress?.Invoke("缺少 CUDA 13/cuDNN 9 运行库，正在准备应用私有环境……");
            var python = await FindPythonAsync(os, cancellationToken);
            if (python is null)
            {
                return new(true, false, false, current + " " + PythonDiscovery.MissingPythonMessage(os));
            }

            Directory.CreateDirectory(InstallDirectory);
            var baseArguments = python.WithArguments(new[] { "-m", "pip", "install", "--upgrade", "--target", InstallDirectory }
                .Concat(RuntimePackages).ToArray());
            string tail = string.Empty;
            for (var attempt = 1; attempt <= PipProxyPolicy.MaxAttempts; attempt++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                progress?.Invoke(attempt == 1 ? "正在下载 CUDA 13/cuDNN 9 运行库……" : "代理下载失败，正在改用直连重试……");
                var arguments = PipProxyPolicy.BuildArguments(baseArguments, attempt, _proxy);
                var environment = PipProxyPolicy.BuildEnvironmentOverrides(attempt, _caBundle);
                var result = await TrainingShell.RunStreamingAsync(
                    python.Executable,
                    arguments,
                    AppContext.BaseDirectory,
                    environment,
                    line => _logger.LogInformation("[CUDA install] {Line}", line),
                    InstallStallTimeout,
                    cancellationToken);
                tail = result.Tail;
                if (result.ExitCode == 0) { break; }
                if (attempt == PipProxyPolicy.MaxAttempts)
                {
                    var detail = LastNonEmptyLine(tail);
                    return new(true, false, false, "CUDA 运行库自动安装失败。" + (detail.Length == 0 ? string.Empty : " " + detail)
                        + " " + PipProxyPolicy.FailureMessage("CUDA 运行库安装"));
                }
            }

            progress?.Invoke("正在验证 CUDA 运行库……");
            var failure = CudaRuntimeLibraries.TryPrepare(AppContext.BaseDirectory, message => _logger.LogInformation("{Message}", message));
            return failure is null
                ? new(true, true, true, ReadyMessage(gpu, os, installed: true))
                : new(true, false, true, "CUDA 运行库已下载，但加载验证仍未通过：" + failure + RuntimePathMessage(os));
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception error)
        {
            _logger.LogError(error, "准备 CUDA 推理环境失败");
            return new(true, false, false, "准备 CUDA 推理环境失败：" + error.Message);
        }
        finally { _gate.Release(); }
    }

    /// <summary>生成包含显卡、计算能力、驱动及推理/训练 CUDA 匹配结果的就绪说明。</summary>
    internal static string ReadyMessage(GpuInfo gpu, OsKind os, bool installed)
    {
        var computeCapability = string.IsNullOrWhiteSpace(gpu.ComputeCap) ? "未知" : gpu.ComputeCap.Trim();
        var trainingChannel = CudaMapping.Select(gpu.ComputeCapParsed, gpu.DriverVersion, os);
        var trainingCuda = trainingChannel == "cpu" ? "CPU" : trainingChannel;
        var fp32 = GpuPerformance.FormatFp32Tflops(gpu);
        var installPrefix = installed ? "CUDA 13/cuDNN 9 运行库安装完成。" : string.Empty;
        return $"{installPrefix}GPU 推理运行库检查通过：{gpu.Name}；计算能力 {computeCapability}；FP32 算力{fp32}；推理 CUDA 13.0 / cuDNN 9；训练 CUDA {trainingCuda}；驱动 {gpu.DriverVersion}。实际模型会话创建成功后才表示本次 GPU 推理可用。";
    }

    /// <summary>探测所选设备，避免多显卡机器始终检查 GPU 0。</summary>
    /// <param name="os">当前操作系统。</param>
    /// <param name="gpuId">待检查的 CUDA 设备编号。</param>
    /// <param name="cancellationToken">取消硬件探测。</param>
    /// <returns>所选设备信息；设备不存在或驱动不可用时为空。</returns>
    private static async Task<GpuInfo?> DetectGpuAsync(OsKind os, int gpuId, CancellationToken cancellationToken)
    {
        foreach (var executable in NvidiaSmi.CandidateExecutables(os))
        {
            foreach (var query in new[] { NvidiaSmi.RichQuery, NvidiaSmi.LegacyQuery })
            {
                var result = await TrainingShell.RunAsync(executable, query, ProbeTimeout, cancellationToken);
                if (result.ExitCode != 0) { continue; }
                var gpu = NvidiaSmiParser.ParseCsv(result.Stdout).ElementAtOrDefault(gpuId);
                if (gpu is not null)
                {
                    var performance = NvmlGpuPerformance.TryRead((uint)gpuId);
                    return gpu with { CudaCoreCount = performance.CoreCount, MaxGraphicsClockMhz = performance.MaxGraphicsClockMhz };
                }
            }
        }
        return null;
    }

    private static async Task<PythonLauncher?> FindPythonAsync(OsKind os, CancellationToken cancellationToken)
    {
        foreach (var algorithm in new[] { "yolo", "anomalib" })
        {
            var venvPython = TrainEnvironmentPlanner.VenvPython(Path.Combine(AppContext.BaseDirectory, "train", algorithm, ".env"), os);
            if (!File.Exists(venvPython)) { continue; }
            var ready = await TrainingShell.RunAsync(venvPython, new[] { "-m", "pip", "--version" }, ProbeTimeout, cancellationToken);
            if (ready.ExitCode == 0) { return new PythonLauncher(venvPython, Array.Empty<string>()); }
        }
        foreach (var probe in PythonDiscovery.VersionProbes(os))
        {
            var version = await TrainingShell.RunAsync(probe.Launcher.Executable, probe.Arguments, ProbeTimeout, cancellationToken);
            if (version.ExitCode != 0 || !PythonDiscovery.IsPython3(version.Stdout, version.Stderr)) { continue; }
            var pip = await TrainingShell.RunAsync(probe.Launcher.Executable,
                probe.Launcher.WithArguments(new[] { "-m", "pip", "--version" }), ProbeTimeout, cancellationToken);
            if (pip.ExitCode == 0) { return probe.Launcher; }
        }
        return null;
    }

    internal static string MissingDriverMessage(OsKind os, bool wsl) => wsl
        ? "WSL 未检测到 NVIDIA GPU。请在 Windows 宿主机安装/更新 NVIDIA 驱动并执行 wsl --update；不要在 WSL 内安装 Linux 显卡驱动。"
        : os == OsKind.Windows
            ? "未检测到可用的 NVIDIA 驱动。请从 NVIDIA 官方驱动页面安装最新驱动，重启后再试。"
            : "未检测到可用的 NVIDIA 驱动。Ubuntu/Debian 可执行 ubuntu-drivers devices 后安装推荐驱动；Fedora/RHEL、SUSE、Arch 请使用各发行版的 NVIDIA 官方/维护者驱动仓库，重启后再试。";

    internal static string DriverUpdateMessage(OsKind os, bool wsl) => wsl
        ? "请更新 Windows 宿主机的 NVIDIA 驱动并执行 wsl --update；不要在 WSL 内安装 Linux 驱动。"
        : os == OsKind.Windows
            ? "请更新 NVIDIA Windows 驱动并重启。"
            : "请通过当前 Linux 发行版的 NVIDIA 驱动仓库更新驱动并重启；容器环境还需在宿主机配置 NVIDIA Container Toolkit。";

    private static string RuntimePathMessage(OsKind os) => os == OsKind.Windows
        ? " 请确认 Visual C++ 2019/2022 x64 Runtime 已安装，然后重启应用。"
        : " 请确认系统已安装 zlib（Ubuntu/Debian: sudo apt install -y zlib1g；Fedora/RHEL: sudo dnf install -y zlib）。";

    private static bool IsWsl()
    {
        if (!OperatingSystem.IsLinux()) { return false; }
        if (!string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("WSL_DISTRO_NAME"))) { return true; }
        try { return File.ReadAllText("/proc/version").Contains("microsoft", StringComparison.OrdinalIgnoreCase); }
        catch { return false; }
    }

    private static string LastNonEmptyLine(string text)
        => text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).LastOrDefault() ?? string.Empty;
}
