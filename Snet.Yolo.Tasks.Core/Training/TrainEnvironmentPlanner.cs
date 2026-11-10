namespace Snet.Yolo.Tasks.Core.Training;

using System;
using System.Collections.Generic;

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
    /// <param name="os">当前操作系统。</param>
    /// <param name="useGpu">是否安装 CUDA 构建。</param>
    /// <param name="channel">已按硬件与驱动选择的 wheel 通道。</param>
    /// <param name="venvPython">独立环境内的 Python 路径。</param>
    /// <param name="latestCompatible">重置环境时安装该通道最新稳定版本；cu118 保留最后适配的版本组合。</param>
    public static SetupCommand? TorchInstallCommand(OsKind os, bool useGpu, string channel, string venvPython, bool latestCompatible = false)
    {
        var versions = TorchVersions(channel);
        var args = new List<string>
        {
            "-m", "pip", "install",
            "torch==" + versions.Torch,
            "torchvision==" + versions.Vision,
            "torchaudio==" + versions.Audio,
        };
        if (latestCompatible && channel != "cu118")
        { args = ["-m", "pip", "install", "--upgrade", "torch", "torchvision", "torchaudio"]; }
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
