namespace Snet.Yolo.Tasks.Core.Training;

using System;
using System.Collections.Generic;
using System.Linq;

/// <summary>从 nvidia-smi 解析出的 GPU 信息。</summary>
public sealed record GpuInfo(string Name, string ComputeCap, string DriverVersion, long? MemoryMb, string? CudaVersion)
{
    /// <summary>Whether a GPU name was detected.</summary>
    public bool HasGpu => !string.IsNullOrWhiteSpace(Name);
    /// <summary>Parsed CUDA compute capability, when available.</summary>
    public double? ComputeCapParsed => double.TryParse(ComputeCap, out var v) ? v : null;
    /// <summary>NVIDIA 驱动报告的 CUDA 核心数量。</summary>
    public uint? CudaCoreCount { get; init; }
    /// <summary>NVIDIA 驱动报告的最大图形时钟，单位 MHz。</summary>
    public uint? MaxGraphicsClockMhz { get; init; }
    /// <summary>按 CUDA 核心数和最大图形时钟估算的 FP32 理论峰值，单位 TFLOPS。</summary>
    public double? TheoreticalFp32Tflops => GpuPerformance.CalculateFp32Tflops(CudaCoreCount, MaxGraphicsClockMhz);
}

/// <summary>GPU 理论峰值计算。</summary>
public static class GpuPerformance
{
    /// <summary>按每个 CUDA 核心每时钟周期执行一次 FMA（两个浮点运算）估算 FP32 峰值。</summary>
    public static double? CalculateFp32Tflops(uint? cudaCoreCount, uint? maxGraphicsClockMhz)
    {
        if (cudaCoreCount is null or 0 || maxGraphicsClockMhz is null or 0) { return null; }
        return cudaCoreCount.Value * maxGraphicsClockMhz.Value * 2d / 1_000_000d;
    }

    /// <summary>格式化用于界面展示的 FP32 理论峰值。</summary>
    public static string FormatFp32Tflops(GpuInfo gpu) => gpu.TheoreticalFp32Tflops is double tflops
        ? $"约 {tflops.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture)} TFLOPS（理论峰值）"
        : "未知（驱动未提供核心数或最大频率）";
}

/// <summary>
/// GPU 计算能力和驱动版本到 PyTorch CUDA wheel 的映射。
/// CUDA 11.8 保留给 Pascal/Volta/Turing，Ampere 及更新架构优先使用与 ONNX Runtime
/// CUDA 12 构建同代的 CUDA 12.8；驱动不足时降级到 CUDA 11.8，而不是安装后才失败。
/// </summary>
public static class CudaMapping
{
    /// <summary>无法判定计算能力时选择兼容面更广的 CUDA 11.8 通道。</summary>
    public const string ConservativeChannel = "cu118";

    /// <summary>CUDA 11.x minor compatibility 所需的最低 Linux 驱动主版本。</summary>
    public const int Cuda11MinimumLinuxDriver = 450;

    /// <summary>CUDA 11.x minor compatibility 所需的最低 Windows 驱动主版本。</summary>
    public const int Cuda11MinimumWindowsDriver = 452;

    /// <summary>CUDA 12.x minor compatibility 所需的最低 Linux 驱动主版本。</summary>
    public const int Cuda12MinimumLinuxDriver = 525;

    /// <summary>CUDA 12.x minor compatibility 所需的最低 Windows 驱动主版本。</summary>
    public const int Cuda12MinimumWindowsDriver = 527;

    /// <summary>Maps a CUDA compute capability to a compatible PyTorch wheel channel.</summary>
    public static string Map(double? computeCap)
    {
        if (computeCap is null) { return "cpu"; }
        return computeCap switch
        {
            < 6.0 => "cpu",
            < 7.0 => "cu118",
            < 8.0 => "cu118",
            _ => "cu128",
        };
    }

    /// <summary>
    /// 同时考虑计算能力和当前驱动选择 wheel 通道。低于计算能力 6.0 的旧卡不再安装
    /// 已停止维护的 CUDA 10.2/PyTorch 旧包，直接使用现代 CPU 构建。
    /// </summary>
    public static string Select(double? computeCap, string? driverVersion, OsKind os)
    {
        if (computeCap is null) { return DriverSupports(ConservativeChannel, driverVersion, os) ? ConservativeChannel : "cpu"; }
        var preferred = Map(computeCap);
        if (preferred == "cpu") { return preferred; }
        if (DriverSupports(preferred, driverVersion, os)) { return preferred; }
        return computeCap < 10 && DriverSupports("cu118", driverVersion, os) ? "cu118" : "cpu";
    }

    /// <summary>判断 NVIDIA 驱动是否满足指定 CUDA 主版本的最低要求。</summary>
    public static bool DriverSupports(string channel, string? driverVersion, OsKind os)
    {
        var major = DriverMajor(driverVersion);
        if (major is null) { return true; }
        var minimum = channel.StartsWith("cu12", StringComparison.Ordinal)
            ? (os == OsKind.Windows ? Cuda12MinimumWindowsDriver : Cuda12MinimumLinuxDriver)
            : (os == OsKind.Windows ? Cuda11MinimumWindowsDriver : Cuda11MinimumLinuxDriver);
        return major >= minimum;
    }

    /// <summary>判断已安装 torch 报告的 CUDA 版本是否与计划通道属于同一 CUDA 系列。</summary>
    public static bool RuntimeMatches(string channel, string? runtimeVersion)
    {
        if (channel == "cpu") { return true; }
        if (string.IsNullOrWhiteSpace(runtimeVersion)) { return false; }
        return channel.StartsWith("cu11", StringComparison.Ordinal) ? runtimeVersion.StartsWith("11.", StringComparison.Ordinal)
            : channel.StartsWith("cu12", StringComparison.Ordinal) && runtimeVersion.StartsWith("12.", StringComparison.Ordinal);
    }

    /// <summary>解析形如 535.104.05 或 551.86 的驱动主版本。</summary>
    public static int? DriverMajor(string? driverVersion)
    {
        if (string.IsNullOrWhiteSpace(driverVersion)) { return null; }
        var first = driverVersion.Trim().Split('.')[0];
        return int.TryParse(first, out var value) ? value : null;
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
    /// <summary>Parses one or more GPUs from CSV output produced by <c>nvidia-smi</c>.</summary>
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
