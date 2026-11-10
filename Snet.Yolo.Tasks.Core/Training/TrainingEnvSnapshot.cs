namespace Snet.Yolo.Tasks.Core.Training;

using System;
using System.Collections.Generic;

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
