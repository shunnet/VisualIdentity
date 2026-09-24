namespace Snet.Yolo.Tasks.Core.Training;

using System;
using System.Collections.Generic;

/// <summary>操作系统类型。</summary>
public enum OsKind
{
    /// <summary>Microsoft Windows.</summary>
    Windows,
    /// <summary>Linux.</summary>
    Linux,
    /// <summary>Apple macOS.</summary>
    Mac,
}

/// <summary>训练环境检测快照（由 App 层执行检测后填充）。</summary>
public sealed class TrainingEnvSnapshot
{
    /// <summary>Detected operating system.</summary>
    public OsKind Os { get; set; } = OsKind.Windows;
    /// <summary>用于调用 Python 的命令（如 "python" / "python3" / 完整路径）。</summary>
    public string PythonCmd { get; set; } = "python";
    /// <summary>调用 Python 时的固定前缀参数（Windows 的 py -3 为 ["-3"]）。</summary>
    public IReadOnlyList<string> PythonArguments { get; set; } = Array.Empty<string>();
    /// <summary>Whether Python is available.</summary>
    public bool HasPython { get; set; }
    /// <summary>Whether pip is available.</summary>
    public bool HasPip { get; set; }
    /// <summary>系统 Python 是否具备 venv 模块（Debian 上需单独安装 python3-venv）。</summary>
    public bool HasVenv { get; set; }
    /// <summary>Detected NVIDIA GPU, if any.</summary>
    public GpuInfo? Gpu { get; set; }
    /// <summary>是否可用 Apple MPS（仅 macOS，需要 venv 内已装 torch）。</summary>
    public bool HasMps { get; set; }
    /// <summary>Whether system Python has PyTorch.</summary>
    public bool TorchInstalled { get; set; }
    /// <summary>Whether system Python has Ultralytics.</summary>
    public bool UltralyticsInstalled { get; set; }
    /// <summary>Whether PyTorch reports CUDA availability.</summary>
    public bool TorchCudaAvailable { get; set; }
    /// <summary>venv 内 PyTorch 报告的 CUDA 运行时版本；CPU 构建为空。</summary>
    public string? TorchCudaVersion { get; set; }
    /// <summary>venv 目录绝对路径（用户目录，跨项目复用）。</summary>
    public string VenvPath { get; set; } = string.Empty;
    /// <summary>venv 目录是否存在（可能残留/损坏，需要先删后建）。</summary>
    public bool VenvDirectoryExists { get; set; }
    /// <summary>Whether the virtual environment is usable.</summary>
    public bool VenvExists { get; set; }
    /// <summary>Whether the virtual environment contains PyTorch.</summary>
    public bool VenvHasTorch { get; set; }
    /// <summary>Whether the virtual environment contains Ultralytics.</summary>
    public bool VenvHasUltralytics { get; set; }
}

/// <summary>Kind of environment-setup operation.</summary>
public enum SetupStepKind
{
    /// <summary>Informational step.</summary>
    Info,
    /// <summary>Create or recreate the Python virtual environment.</summary>
    RecreateVenv,
    /// <summary>Install PyTorch packages.</summary>
    PipInstallTorch,
    /// <summary>Install Ultralytics.</summary>
    PipInstallYolo,
    /// <summary>安装固定版本的 Anomalib 与 ONNX 导出依赖。</summary>
    PipInstallAnomalib,
    /// <summary>Install the Visual C++ runtime.</summary>
    InstallVcRedist,
}

/// <summary>一条环境搭建命令：可执行文件 + 参数列表（避免命令行字符串往返解析导致路径带空格时出错）。</summary>
/// <param name="Executable">可执行文件（可为空，表示仅提示的 Info 步骤）。</param>
/// <param name="ArgumentList">显式参数列表，直接写入 ProcessStartInfo.ArgumentList。</param>
/// <param name="IsNetwork">是否为依赖网络的 pip 安装步骤（失败时按代理重试阶梯重试）。</param>
public sealed record SetupCommand(string Executable, IReadOnlyList<string> ArgumentList, bool IsNetwork = false)
{
    /// <summary>仅用于日志展示的命令行文本。</summary>
    public string Arguments => CommandLine.JoinArguments(ArgumentList);
}

/// <summary>One ordered environment-setup step.</summary>
/// <param name="Kind">Operation kind.</param>
/// <param name="Description">User-facing description.</param>
/// <param name="Command">Command to execute.</param>
public sealed record SetupStep(SetupStepKind Kind, string Description, SetupCommand Command)
{
    /// <summary>该步骤是否为可重试的网络 pip 安装。</summary>
    public bool IsNetwork => Command.IsNetwork;
}

/// <summary>Complete training-environment plan.</summary>
/// <param name="EnvReady">Whether the environment is ready.</param>
/// <param name="UseGpu">Whether CUDA is selected.</param>
/// <param name="Device">Ultralytics device argument.</param>
/// <param name="CudaVersion">Selected PyTorch channel or accelerator.</param>
/// <param name="VenvPython">Virtual-environment Python path.</param>
/// <param name="VenvYolo">Virtual-environment YOLO path.</param>
/// <param name="Warning">Optional user-facing warning.</param>
/// <param name="Steps">Ordered setup steps.</param>
public sealed record SetupPlan(
    bool EnvReady,
    bool UseGpu,
    string Device,
    string CudaVersion,
    string VenvPython,
    string VenvYolo,
    string? Warning,
    IReadOnlyList<SetupStep> Steps)
{
    /// <summary>是否具备硬件加速后端（CUDA 或 Apple MPS；macOS 的 MPS 不算 CUDA GPU）。</summary>
    public bool Accelerated => UseGpu || string.Equals(Device, "mps", StringComparison.OrdinalIgnoreCase);
}

/// <summary>规划：环境是否就绪、设备选择、若未就绪则生成搭建步骤（OS 相关）。</summary>
public static class TrainEnvironmentPlanner
{
    /// <summary>Builds an environment plan from the detected snapshot.</summary>
    public static SetupPlan Plan(TrainingEnvSnapshot snap)
    {
        // macOS 的 MPS 不是 NVIDIA GPU：既不安装 CUDA wheel，也不把 UseGpu 置为 true
        var hasCuda = snap.Os != OsKind.Mac && snap.Gpu is { HasGpu: true };
        var hasMps = snap.Os == OsKind.Mac && snap.HasMps;
        var channel = hasCuda
            ? CudaMapping.Select(CudaMapping.TryParse(snap.Gpu?.ComputeCap), snap.Gpu?.DriverVersion, snap.Os)
            : "cpu";
        var useGpu = hasCuda && channel != "cpu";
        var device = useGpu ? "0" : hasMps ? "mps" : "cpu";
        var warning = useGpu || hasMps ? null : hasCuda
            ? "检测到 NVIDIA GPU，但其计算能力或驱动不满足当前 PyTorch/CUDA 版本，已改用 CPU 训练。"
            : "当前走 CPU 训练，速度较慢、效率较低。";
        var cuda = hasMps ? "mps" : channel;
        var venvPython = VenvPython(snap.VenvPath, snap.Os);
        var venvYolo = VenvYolo(snap.VenvPath, snap.Os);

        var torchMatches = !useGpu || (snap.TorchCudaAvailable && CudaMapping.RuntimeMatches(channel, snap.TorchCudaVersion));
        var envReady = snap.VenvExists && snap.VenvHasTorch && snap.VenvHasUltralytics && torchMatches;
        var steps = new List<SetupStep>();

        if (!envReady)
        {
            // 缺少 Python 或 venv 模块时给出该系统可直接执行的安装指引
            if (!snap.HasPython)
            {
                steps.Add(new SetupStep(SetupStepKind.Info, PythonDiscovery.MissingPythonMessage(snap.Os),
                    new SetupCommand(string.Empty, Array.Empty<string>())));
            }
            else if (!snap.HasVenv)
            {
                steps.Add(new SetupStep(SetupStepKind.Info, PythonDiscovery.MissingVenvMessage(snap.Os),
                    new SetupCommand(string.Empty, Array.Empty<string>())));
            }

            var venvArguments = new List<string>(snap.PythonArguments ?? Array.Empty<string>()) { "-m", "venv", snap.VenvPath };
            steps.Add(new SetupStep(SetupStepKind.RecreateVenv,
                snap.VenvDirectoryExists || snap.VenvExists ? "重建虚拟环境（venv）" : "创建虚拟环境（venv）",
                new SetupCommand(snap.PythonCmd, venvArguments)));

            var torchCmd = TorchInstallCommand(snap.Os, useGpu, channel, venvPython);
            if (torchCmd is not null)
            {
                steps.Add(new SetupStep(SetupStepKind.PipInstallTorch,
                    "安装 " + DescribeTorch(useGpu, hasMps, channel) + " 版 PyTorch", torchCmd));
            }
            steps.Add(new SetupStep(SetupStepKind.PipInstallYolo,
                "安装最新版 Ultralytics",
                new SetupCommand(venvPython, new[] { "-m", "pip", "install", "ultralytics" }, IsNetwork: true)));
        }

        return new SetupPlan(envReady, useGpu, device, cuda, venvPython, venvYolo, warning, steps);
    }

    /// <summary>PyTorch 安装步骤的中文描述。</summary>
    public static string DescribeTorch(bool useGpu, bool hasMps, string channel)
        => useGpu ? "GPU(CUDA " + channel + ")" : hasMps ? "Apple MPS" : "CPU";

    /// <summary>Returns the virtual environment's Python executable.</summary>
    public static string VenvPython(string venvPath, OsKind os)
        => os == OsKind.Windows
            ? System.IO.Path.Combine(venvPath, "Scripts", "python.exe")
            : System.IO.Path.Combine(venvPath, "bin", "python");
    /// <summary>Returns the virtual environment's Ultralytics executable.</summary>
    public static string VenvYolo(string venvPath, OsKind os)
        => os == OsKind.Windows
            ? System.IO.Path.Combine(venvPath, "Scripts", "yolo.exe")
            : System.IO.Path.Combine(venvPath, "bin", "yolo");

    /// <summary>
    /// PyTorch 安装命令。macOS（含 Apple Silicon）的 wheel 只发布在默认 PyPI 索引上，
    /// 不能使用 download.pytorch.org/whl/cpu，因此 macOS 一律不加 --index-url。
    /// </summary>
    public static SetupCommand? TorchInstallCommand(OsKind os, bool useGpu, string channel, string venvPython)
    {
        var versions = TorchVersions(channel);
        var args = new List<string>
        {
            "-m", "pip", "install",
            "torch==" + versions.Torch,
            "torchvision==" + versions.Vision,
            "torchaudio==" + versions.Audio,
        };
        if (os != OsKind.Mac)
        {
            args.Add("--index-url");
            args.Add(useGpu
                ? "https://download.pytorch.org/whl/" + channel
                : "https://download.pytorch.org/whl/cpu");
        }
        return new SetupCommand(venvPython, args, IsNetwork: true);
    }

    /// <summary>返回经过验证的 PyTorch/torchvision/torchaudio 版本组合。</summary>
    public static (string Torch, string Vision, string Audio) TorchVersions(string channel) => channel switch
    {
        "cu118" => ("2.7.1", "0.22.1", "2.7.1"),
        "cu128" => ("2.9.0", "0.24.0", "2.9.0"),
        _ => ("2.9.0", "0.24.0", "2.9.0"),
    };
}
