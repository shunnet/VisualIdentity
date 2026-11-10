namespace Snet.Yolo.Tasks.Core.Anomalib;

using Snet.Yolo.Tasks.Core.Training;

/// <summary>规划独立、版本锁定且不污染 YOLO 环境的 Anomalib Python 环境。</summary>
public static class AnomalibEnvironmentPlanner
{
    /// <summary>经过应用适配验证的 Anomalib 固定版本。</summary>
    public const string AnomalibVersion = "2.6.2";

    /// <summary>ONNX Runtime 与现有 .NET 推理端保持一致的固定版本。</summary>
    public const string OnnxRuntimeVersion = "1.31.1";

    /// <summary>实际导入 ONNX Runtime，避免只安装部分依赖的虚拟环境被误判为就绪。</summary>
    public const string EnvironmentProbeScript = "import importlib.metadata as m; import torch; import onnxruntime as ort; print(m.version('anomalib')); print(ort.__version__); print(torch.cuda.is_available()); print(torch.version.cuda or 'cpu')";

    /// <summary>检查依赖版本及所选 PyTorch CUDA 通道，决定能否复用现有环境。</summary>
    /// <param name="output">成功执行环境探测脚本的标准输出。</param>
    /// <param name="channel">计划使用的 PyTorch 通道。</param>
    /// <returns>依赖版本及 CUDA 状态均符合要求时返回 true。</returns>
    public static bool IsEnvironmentReady(string output, string channel)
    {
        var lines = output.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return lines.Length >= 4 && lines[0] == AnomalibVersion && lines[1] == OnnxRuntimeVersion
            && (channel == "cpu" || lines[2].Equals("True", StringComparison.OrdinalIgnoreCase)
                && CudaMapping.RuntimeMatches(channel, lines[3]));
    }

    /// <summary>建立独立虚拟环境的显式命令计划。</summary>
    /// <param name="applicationDirectory">应用根目录。</param>
    /// <param name="launcher">已探测到的 Python 3 启动器。</param>
    /// <param name="os">当前操作系统。</param>
    /// <param name="torchChannel">PyTorch 通道：cpu、cu118、cu126 或 cu128。</param>
    /// <returns>不包含 shell 字符串的环境计划。</returns>
    public static AnomalibEnvironmentPlan Create(
        string applicationDirectory,
        PythonLauncher launcher,
        OsKind os,
        string torchChannel)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(applicationDirectory);
        ArgumentNullException.ThrowIfNull(launcher);
        var channel = os == OsKind.Mac ? "pypi" : NormalizeTorchChannel(torchChannel);
        var venv = Path.Combine(Path.GetFullPath(applicationDirectory), "train", "anomalib", ".env");
        var python = os == OsKind.Windows
            ? Path.Combine(venv, "Scripts", "python.exe")
            : Path.Combine(venv, "bin", "python");
        var createArguments = launcher.WithArguments(["-m", "venv", venv]);
        var torchArguments = new List<string>
        {
            "-m", "pip", "install", "--disable-pip-version-check", "--no-cache-dir",
            "torch", "torchvision",
        };
        if (channel != "pypi")
        {
            torchArguments.Add("--index-url");
            torchArguments.Add(channel == "cpu"
                ? "https://download.pytorch.org/whl/cpu"
                : "https://download.pytorch.org/whl/" + channel);
        }
        var anomalibArguments = new List<string>
        {
            "-m", "pip", "install", "--disable-pip-version-check", "--no-cache-dir",
            "anomalib==" + AnomalibVersion,
            "onnx>=1.16,<2",
            "onnxscript>=0.3,<1",
            "onnxruntime==" + OnnxRuntimeVersion,
        };
        return new AnomalibEnvironmentPlan
        {
            VenvDirectory = venv,
            VenvPython = python,
            Steps =
            [
                new SetupStep(SetupStepKind.RecreateVenv, "创建独立 Anomalib 虚拟环境", new SetupCommand(launcher.Executable, createArguments)),
                new SetupStep(SetupStepKind.PipInstallTorch, "安装 Anomalib 专用 PyTorch", new SetupCommand(python, torchArguments, true)),
                new SetupStep(SetupStepKind.PipInstallAnomalib, "安装固定版本 Anomalib 与 ONNX 工具", new SetupCommand(python, anomalibArguments, true)),
            ],
        };
    }

    /// <summary>规范化允许的 PyTorch 官方 wheel 通道，未知值安全降级为 CPU。</summary>
    private static string NormalizeTorchChannel(string? channel) => channel?.ToLowerInvariant() switch
    {
        "cu118" => "cu118",
        "cu126" => "cu126",
        "cu128" => "cu128",
        _ => "cpu",
    };
}
