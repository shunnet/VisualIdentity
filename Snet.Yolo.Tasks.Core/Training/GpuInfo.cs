namespace Snet.Yolo.Tasks.Core.Training;

using System;
using System.Collections.Generic;

/// <summary>从 nvidia-smi 解析出的 GPU 信息。</summary>
public sealed record GpuInfo(string Name, string ComputeCap, string DriverVersion, long? MemoryMb, string? CudaVersion)
{
    public bool HasGpu => !string.IsNullOrWhiteSpace(Name);
    public double? ComputeCapParsed => double.TryParse(ComputeCap, out var v) ? v : null;
}

/// <summary>GPU 计算能力 -> PyTorch CUDA wheel 版本（来源：YOLOCPU训练安装包.txt）。</summary>
public static class CudaMapping
{
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

    /// <summary>常见 compute_cap 可能形如 8.6 / 6.1，返回有效数值。</summary>
    public static double? TryParse(string? computeCap)
    {
        if (string.IsNullOrWhiteSpace(computeCap)) { return null; }
        var s = computeCap.Trim().Replace(",", ".");
        return double.TryParse(s, out var v) ? v : null;
    }
}

/// <summary>解析 nvidia-smi --query-gpu ... --format=csv 输出（多行）。</summary>
public static class NvidiaSmiParser
{
    public static IReadOnlyList<GpuInfo> ParseCsv(string output)
    {
        var list = new List<GpuInfo>();
        foreach (var rawLine in output.Split('\n'))
        {
            var line = rawLine.Trim();
            if (line.Length == 0 || line.StartsWith("name", StringComparison.OrdinalIgnoreCase)) { continue; }
            var cols = line.Split(',');
            if (cols.Length < 3) { continue; }
            string Get(int i) => cols.Length > i ? cols[i].Trim() : string.Empty;
            var mem = long.TryParse(Get(3).Replace(" MiB", "").Trim(), out var m) ? m : (long?)null;
            list.Add(new GpuInfo(Get(0), Get(1), Get(2), mem, null));
        }
        return list;
    }
}
