namespace Snet.Yolo.Tasks.Services;

using Snet.Yolo.Server.inference;
using Snet.Yolo.Server.sam;
using Snet.Yolo.Tasks.Core.Anomalib;
using Snet.Yolo.Tasks.Core.Training;

/// <summary>
/// 环境自检：只读地检查 SAM、YOLO、Anomalib 的依赖是否齐全、GPU 是否真的可用
/// （不只看 CUDA 是否可见，还在 GPU 上执行真实运算），供设置页展示并决定是否需要重置。
/// </summary>
/// <param name="metrics">系统资源采样，提供宿主机 GPU 列表。</param>
/// <param name="samModels">SAM 模型存储，提供模型目录。</param>
public sealed class EnvironmentDiagnosticsService(SystemMetrics metrics, SamModelStore samModels)
{
    /// <summary>首次导入 PyTorch 较慢，给探测脚本留足时间。</summary>
    private static readonly TimeSpan ProbeTimeout = TimeSpan.FromSeconds(120);

    /// <summary>每个组件同一时间只做一次自检：并发点击排队执行，不会同时拉起多个导入 PyTorch 的进程。</summary>
    private readonly IReadOnlyDictionary<EnvironmentComponent, SemaphoreSlim> _gates =
        Enum.GetValues<EnvironmentComponent>().ToDictionary(static component => component, static _ => new SemaphoreSlim(1, 1));

    /// <summary>检查一个组件。</summary>
    /// <param name="component">要检查的组件。</param>
    /// <param name="cancellationToken">取消检查。</param>
    /// <returns>自检报告。</returns>
    public async Task<EnvironmentReport> CheckAsync(EnvironmentComponent component, CancellationToken cancellationToken = default)
    {
        var gate = _gates[component];
        await gate.WaitAsync(cancellationToken);
        try
        {
            var gpus = (await metrics.RefreshGpuAsync(cancellationToken)).Gpus;
            var items = component == EnvironmentComponent.Sam
                ? CheckSam(gpus)
                : await CheckPythonEnvironmentAsync(component, gpus, cancellationToken);
            return new EnvironmentReport(component, items, gpus.Count > 0, DateTime.UtcNow);
        }
        finally { gate.Release(); }
    }

    /// <summary>SAM：发行包是否带 CUDA、宿主机 GPU、应用私有的 CUDA 13 / cuDNN 9 运行库能否加载、模型文件。</summary>
    private IReadOnlyList<EnvironmentCheckItem> CheckSam(IReadOnlyList<GpuMetrics> gpus)
    {
        var hostHasGpu = gpus.Count > 0;
        var supportsCuda = OnnxSessionOptions.SupportsCuda;
        var runtimeFailure = supportsCuda ? CudaRuntimeLibraries.TryPrepare(AppContext.BaseDirectory) : "当前发行包不含 CUDA 执行提供程序。";
        var modelFiles = Directory.Exists(samModels.DirectoryPath)
            ? Directory.EnumerateFiles(samModels.DirectoryPath, "*.onnx", SearchOption.AllDirectories).Count() : 0;
        return
        [
            new("Execution", supportsCuda, supportsCuda ? "ONNX Runtime CUDA" : "ONNX Runtime CPU", Required: false),
            new("GpuDriver", hostHasGpu, hostHasGpu ? string.Join("；", gpus.Select(static gpu => $"GPU {gpu.Index} · {gpu.Name}")) : "未检测到 NVIDIA GPU", Required: false),
            new("GpuRuntime", runtimeFailure is null, runtimeFailure is null ? "CUDA 13 / cuDNN 9 运行库可加载" : FirstSentence(runtimeFailure), Required: supportsCuda && hostHasGpu),
            new("Models", modelFiles > 0, modelFiles > 0 ? $"{modelFiles} 个 ONNX 文件" : "尚未下载（首次启用 SAM 时自动下载）", Required: false),
        ];
    }

    /// <summary>YOLO / Anomalib：虚拟环境、PyTorch 的 CUDA 构建与 GPU 运算、关键包版本。</summary>
    private async Task<IReadOnlyList<EnvironmentCheckItem>> CheckPythonEnvironmentAsync(EnvironmentComponent component, IReadOnlyList<GpuMetrics> gpus, CancellationToken cancellationToken)
    {
        var algorithm = component == EnvironmentComponent.Yolo ? "yolo" : "anomalib";
        var os = OperatingSystem.IsWindows() ? OsKind.Windows : OperatingSystem.IsMacOS() ? OsKind.Mac : OsKind.Linux;
        var python = TrainEnvironmentPlanner.VenvPython(Path.Combine(AppContext.BaseDirectory, "train", algorithm, ".env"), os);
        if (!File.Exists(python)) { return [new("Venv", false, "未安装：" + python)]; }
        var result = await TrainingShell.RunAsync(python, ["-c", PythonEnvironmentProbe.Script], ProbeTimeout, cancellationToken);
        var facts = result.ExitCode == 0 ? PythonEnvironmentProbe.Parse(result.Stdout) : null;
        if (facts is null) { return [new("Venv", true, python), new("Torch", false, "探测失败：" + FirstLine(result.Stderr, result.Stdout))]; }
        return BuildPythonItems(component, python, facts, gpus.Count);
    }

    /// <summary>把探测事实转换为逐项检查结果；纯函数，便于单元测试。</summary>
    /// <param name="component">YOLO 或 Anomalib。</param>
    /// <param name="python">虚拟环境的 Python 路径。</param>
    /// <param name="facts">探测脚本输出的事实。</param>
    /// <param name="hostGpuCount">宿主机检测到的 NVIDIA GPU 数量。</param>
    /// <returns>逐项检查结果。</returns>
    internal static IReadOnlyList<EnvironmentCheckItem> BuildPythonItems(EnvironmentComponent component, string python, PythonEnvironmentFacts facts, int hostGpuCount)
    {
        var hostHasGpu = hostGpuCount > 0;
        var torchOk = facts.Torch is not null;
        var cudaBuild = torchOk && facts.TorchCuda.Length > 0;
        var items = new List<EnvironmentCheckItem>
        {
            new("Venv", true, python),
            new("Torch", torchOk, torchOk ? $"{facts.Torch}（{(cudaBuild ? "CUDA " + facts.TorchCuda : "CPU 构建")}）" : facts.TorchError ?? "未安装"),
            new("GpuTorch", facts.CudaAvailable, facts.CudaAvailable ? "torch.cuda.is_available() = True" : cudaBuild ? "CUDA 构建但不可用：请检查驱动" : "当前是 CPU 构建，无法使用 GPU", Required: hostHasGpu),
            new("GpuKernel", facts.KernelOk, facts.KernelOk ? "GPU 上的张量运算成功" : "尚未验证", Required: hostHasGpu && facts.CudaAvailable),
            new("GpuDevices", facts.Devices.Count > 0, facts.Devices.Count > 0 ? string.Join("；", facts.Devices) : "PyTorch 看不到 GPU", Required: hostHasGpu),
        };
        if (component == EnvironmentComponent.Yolo)
        {
            items.Add(new("Ultralytics", facts.Ultralytics is not null, facts.Ultralytics ?? "未安装"));
        }
        else
        {
            items.Add(new("Anomalib", facts.Anomalib == AnomalibEnvironmentPlanner.AnomalibVersion, (facts.Anomalib ?? "未安装") + "（需要 " + AnomalibEnvironmentPlanner.AnomalibVersion + "）"));
            items.Add(new("OnnxRuntime", facts.OnnxRuntime == AnomalibEnvironmentPlanner.OnnxRuntimeVersion, (facts.OnnxRuntime ?? "未安装") + "（需要 " + AnomalibEnvironmentPlanner.OnnxRuntimeVersion + "）"));
        }
        return items;
    }

    /// <summary>只保留第一句：运行库缺失的原始说明会附带搜索过的全部目录，界面上只需要“缺什么”。</summary>
    /// <param name="message">原始说明。</param>
    /// <returns>第一个句号（含）之前的文本；没有句号时返回原文。</returns>
    internal static string FirstSentence(string message)
    {
        var end = message.IndexOf('。');
        return end > 0 ? message[..(end + 1)] : message;
    }

    /// <summary>取输出里第一行非空文本，用于单行说明。</summary>
    private static string FirstLine(string primary, string fallback)
        => new[] { primary, fallback }.SelectMany(static text => text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)).FirstOrDefault() ?? string.Empty;
}
