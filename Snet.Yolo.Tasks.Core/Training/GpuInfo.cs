namespace Snet.Yolo.Tasks.Core.Training;

using System;
using System.Collections.Generic;
using System.Linq;

/// <summary>从 nvidia-smi 解析出的 GPU 信息。</summary>
public sealed record GpuInfo(string Name, string ComputeCap, string DriverVersion, long? MemoryMb, string? CudaVersion)
{
    public bool HasGpu => !string.IsNullOrWhiteSpace(Name);
    public double? ComputeCapParsed => double.TryParse(ComputeCap, out var v) ? v : null;
}

/// <summary>GPU 计算能力 -> PyTorch CUDA wheel 版本（来源：YOLOCPU训练安装包.txt）。</summary>
public static class CudaMapping
{
    /// <summary>无法判定计算能力（旧驱动无 compute_cap）时保守选择的通道：cu121 能被较新的驱动普遍支持。</summary>
    public const string ConservativeChannel = "cu121";

    public static string Map(double? computeCap)
    {
        if (computeCap is null) { return "cpu"; }
        return computeCap switch
        {
            < 6.0 => "cu102",
            < 7.0 => "cu118",
            < 8.0 => "cu121",
            _ => "cu124",
        };
    }

    /// <summary>已知存在 GPU 但无法确定计算能力时的保守通道（不要静默降级为 CPU）。</summary>
    public static string MapUnknown() => ConservativeChannel;

    /// <summary>常见 compute_cap 可能形如 8.6 / 6.1，返回有效数值。</summary>
    public static double? TryParse(string? computeCap)
    {
        if (string.IsNullOrWhiteSpace(computeCap)) { return null; }
        var s = computeCap.Trim().Replace(",", ".");
        return double.TryParse(s, out var v) ? v : null;
    }
}

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

/// <summary>解析 nvidia-smi --query-gpu ... --format=csv 输出（多行、多 GPU、旧驱动无 compute_cap）。</summary>
public static class NvidiaSmiParser
{
    public static IReadOnlyList<GpuInfo> ParseCsv(string output)
    {
        var list = new List<GpuInfo>();
        if (string.IsNullOrWhiteSpace(output)) { return list; }

        string[]? header = null;
        foreach (var rawLine in output.Split('\n'))
        {
            var line = rawLine.Trim();
            if (line.Length == 0) { continue; }
            var cols = line.Split(',').Select(c => c.Trim()).ToArray();

            // 表头形如 name, compute_cap, driver_version, memory.total；
            // 降级查询则为 name, driver_version, memory.total（列数不同，必须按表头定位）。
            if (header is null && cols[0].Equals("name", StringComparison.OrdinalIgnoreCase))
            {
                header = cols.Select(c => c.ToLowerInvariant()).ToArray();
                continue;
            }
            if (cols.Length < 2) { continue; }

            int nameIndex = 0, capIndex = -1, driverIndex = -1, memoryIndex = -1;
            if (header is null)
            {
                // 没有表头：按列数推断（>=4 视为含 compute_cap）
                if (cols.Length >= 4) { nameIndex = 0; capIndex = 1; driverIndex = 2; memoryIndex = 3; }
                else { nameIndex = 0; driverIndex = 1; memoryIndex = 2; }
            }
            else
            {
                nameIndex = IndexOf(header, "name");
                capIndex = IndexOf(header, "compute_cap");
                driverIndex = IndexOf(header, "driver_version");
                memoryIndex = IndexOf(header, "memory.total");
                if (nameIndex < 0) { nameIndex = 0; }
            }

            string Cell(int index) => index >= 0 && cols.Length > index ? cols[index] : string.Empty;
            var memoryText = Cell(memoryIndex).Replace("MiB", string.Empty, StringComparison.OrdinalIgnoreCase).Trim();
            var memory = long.TryParse(memoryText, out var parsedMemory) ? parsedMemory : (long?)null;
            list.Add(new GpuInfo(Cell(nameIndex), Cell(capIndex), Cell(driverIndex), memory, null));
        }
        return list;
    }

    private static int IndexOf(string[] header, string key)
        => Array.FindIndex(header, h => string.Equals(h, key, StringComparison.OrdinalIgnoreCase));
}
