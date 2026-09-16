namespace Snet.Yolo.Tasks.Core.Training;

using System.Collections.Generic;

/// <summary>训练运行时探测：Windows/Linux 检测 CUDA，macOS 检测 Apple MPS。</summary>
public static class TorchRuntimeProbe
{
    /// <summary>探测脚本：先打印加速后端可用性，再打印 ultralytics 版本。</summary>
    public static string Script(OsKind os) => os == OsKind.Mac
        ? "import torch, ultralytics; print(torch.backends.mps.is_available()); print(ultralytics.__version__)"
        : "import torch, ultralytics; print(torch.cuda.is_available()); print(ultralytics.__version__)";

    /// <summary>传给 python 的参数（-c 脚本），作为两个独立参数以避免字符串转义问题。</summary>
    public static IReadOnlyList<string> Arguments(OsKind os) => new[] { "-c", Script(os) };

    /// <summary>把探测结果映射为 Ultralytics 的 device 取值（macOS 用 mps，其余用 0/cpu）。</summary>
    public static string SelectDevice(OsKind os, bool acceleratorAvailable)
        => os == OsKind.Mac
            ? (acceleratorAvailable ? "mps" : "cpu")
            : (acceleratorAvailable ? "0" : "cpu");

    /// <summary>仅探测 MPS 可用性的脚本（macOS 环境检测阶段使用）。</summary>
    public static IReadOnlyList<string> MpsProbeArguments { get; } = new[] { "-c", "import torch; print(torch.backends.mps.is_available())" };
}
