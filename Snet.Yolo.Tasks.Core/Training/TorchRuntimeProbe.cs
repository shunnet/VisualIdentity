namespace Snet.Yolo.Tasks.Core.Training;

using System.Collections.Generic;

/// <summary>训练运行时探测：Windows/Linux 检测 CUDA，macOS 检测 Apple MPS。</summary>
public static class TorchRuntimeProbe
{
    /// <summary>
    /// 探测脚本：只导入 torch，版本号用 importlib.metadata 读取。
    /// 不导入 ultralytics —— 它在导入时会做联网版本检查/字体下载，网络不通或代理黑洞时会长时间卡住，
    /// 而这里只需要一个版本字符串。
    /// </summary>
    public static string Script(OsKind os)
    {
        var backend = os == OsKind.Mac ? "torch.backends.mps.is_available()" : "torch.cuda.is_available()";
        return string.Join('\n', new[]
        {
            "import importlib.metadata as md, torch",
            "print(" + backend + ")",
            "try:",
            "    print(md.version('ultralytics'))",
            "except Exception:",
            "    print('unknown')",
        });
    }

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
