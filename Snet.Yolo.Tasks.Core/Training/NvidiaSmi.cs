namespace Snet.Yolo.Tasks.Core.Training;

using System.Collections.Generic;

/// <summary>nvidia-smi 的查询参数与候选路径（精简 PATH / 旧驱动兼容）。</summary>
public static class NvidiaSmi
{
    /// <summary>包含 compute_cap 的完整查询。</summary>
    public static IReadOnlyList<string> RichQuery { get; } = new[] { "--query-gpu=name,compute_cap,driver_version,memory.total", "--format=csv" };

    /// <summary>旧驱动不支持 compute_cap 时的降级查询。</summary>
    public static IReadOnlyList<string> LegacyQuery { get; } = new[] { "--query-gpu=name,driver_version,memory.total", "--format=csv" };

    /// <summary>候选可执行文件：先走 PATH，再退化到常见绝对路径（systemd 精简 PATH、WSL、Windows System32）。</summary>
    public static IReadOnlyList<string> CandidateExecutables(OsKind os)
    {
        var candidates = new List<string> { "nvidia-smi" };
        if (os == OsKind.Windows)
        {
            candidates.Add(@"C:\Windows\System32\nvidia-smi.exe");
            candidates.Add(@"C:\Program Files\NVIDIA Corporation\NVSMI\nvidia-smi.exe");
        }
        else
        {
            candidates.Add("/usr/bin/nvidia-smi");
            candidates.Add("/usr/local/bin/nvidia-smi");
            candidates.Add("/usr/local/nvidia/bin/nvidia-smi");
            candidates.Add("/usr/lib/wsl/lib/nvidia-smi");
        }
        return candidates;
    }
}
