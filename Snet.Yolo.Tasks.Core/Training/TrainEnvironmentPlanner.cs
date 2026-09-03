namespace Snet.Yolo.Tasks.Core.Training;

using System.Collections.Generic;

/// <summary>操作系统类型。</summary>
public enum OsKind { Windows, Linux, Mac }

/// <summary>训练环境检测快照（由 App 层执行检测后填充）。</summary>
public sealed class TrainingEnvSnapshot
{
    public OsKind Os { get; set; } = OsKind.Windows;
    /// <summary>用于调用 Python 的命令（如 "python" / "python3" / 完整路径）。</summary>
    public string PythonCmd { get; set; } = "python";
    public bool HasPython { get; set; }
    public bool HasPip { get; set; }
    public GpuInfo? Gpu { get; set; }
    public bool TorchInstalled { get; set; }
    public bool UltralyticsInstalled { get; set; }
    public bool TorchCudaAvailable { get; set; }
    /// <summary>venv 目录绝对路径（用户目录，跨项目复用）。</summary>
    public string VenvPath { get; set; } = string.Empty;
    public bool VenvExists { get; set; }
    public bool VenvHasTorch { get; set; }
    public bool VenvHasUltralytics { get; set; }
}

public enum SetupStepKind { Info, RecreateVenv, PipInstallTorch, PipInstallYolo, InstallVcRedist }

public sealed record SetupCommand(string Executable, string Arguments);
public sealed record SetupStep(SetupStepKind Kind, string Description, SetupCommand Command);
public sealed record SetupPlan(
    bool EnvReady,
    bool UseGpu,
    string Device,
    string CudaVersion,
    string VenvPython,
    string VenvYolo,
    string? Warning,
    IReadOnlyList<SetupStep> Steps);

/// <summary>规划：环境是否就绪、设备选择、若未就绪则生成搭建步骤（OS 相关）。</summary>
public static class TrainEnvironmentPlanner
{
    public static SetupPlan Plan(TrainingEnvSnapshot snap)
    {
        var hasGpu = snap.Gpu is { HasGpu: true };
        var useGpu = hasGpu;
        var device = useGpu ? "0" : "cpu";
        var warning = !useGpu ? "当前走 CPU 训练，速度较慢、效率较低。" : null;
        var cuda = useGpu ? CudaMapping.Map(CudaMapping.TryParse(snap.Gpu?.ComputeCap)) : "cpu";
        var venvPython = VenvPython(snap.VenvPath, snap.Os);
        var venvYolo = VenvYolo(snap.VenvPath, snap.Os);

        var envReady = snap.VenvExists && snap.VenvHasTorch && snap.VenvHasUltralytics;
        var steps = new List<SetupStep>();

        if (!envReady)
        {
            if (!snap.HasPython)
            {
                steps.Add(new SetupStep(SetupStepKind.Info,
                    "未检测到 Python，请先安装 Python 3（Windows：python.org 勾选 Add to PATH；Linux/Ubuntu：sudo apt install -y python3 python3-pip python3-venv）",
                    new SetupCommand(string.Empty, string.Empty)));
            }
            if (snap.VenvExists)
            {
                steps.Add(new SetupStep(SetupStepKind.RecreateVenv,
                    "重建虚拟环境（venv）", new SetupCommand(snap.PythonCmd, "-m venv \"" + snap.VenvPath + "\"")));
            }
            else
            {
                steps.Add(new SetupStep(SetupStepKind.RecreateVenv,
                    "创建虚拟环境（venv）", new SetupCommand(snap.PythonCmd, "-m venv \"" + snap.VenvPath + "\"")));
            }

            var python = venvPython;
            var torchCmd = TorchInstallCommand(useGpu, cuda, python);
            if (torchCmd is not null)
            {
                steps.Add(new SetupStep(SetupStepKind.PipInstallTorch,
                    "安装 " + (useGpu ? "GPU(CUDA " + cuda + ") " : "CPU ") + "版 PyTorch", torchCmd));
            }
            steps.Add(new SetupStep(SetupStepKind.PipInstallYolo,
                "安装最新版 Ultralytics", new SetupCommand(python, "-m pip install ultralytics")));
        }

        return new SetupPlan(envReady, useGpu, device, cuda, venvPython, venvYolo, warning, steps);
    }

    public static string VenvPython(string venvPath, OsKind os)
        => os == OsKind.Windows
            ? System.IO.Path.Combine(venvPath, "Scripts", "python.exe")
            : System.IO.Path.Combine(venvPath, "bin", "python");
    public static string VenvYolo(string venvPath, OsKind os)
        => os == OsKind.Windows
            ? System.IO.Path.Combine(venvPath, "Scripts", "yolo.exe")
            : System.IO.Path.Combine(venvPath, "bin", "yolo");

    private static SetupCommand? TorchInstallCommand(bool useGpu, string cuda, string python)
    {
        var baseArgs = "-m pip install torch torchvision torchaudio";
        if (useGpu)
        {
            // 采用与 CUDA 兼容、可被最新 Ultralytics 接受的版本（不锁安装包旧版本）
            return new SetupCommand(python, baseArgs + " --index-url https://download.pytorch.org/whl/" + cuda);
        }
        return new SetupCommand(python, baseArgs + " --index-url https://download.pytorch.org/whl/cpu");
    }
}
