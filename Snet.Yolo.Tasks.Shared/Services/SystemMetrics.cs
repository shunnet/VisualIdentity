using System.Globalization;
using System.Runtime.InteropServices;
using Snet.Yolo.Tasks.Core.Training;

namespace Snet.Yolo.Tasks.Services;

public sealed record GpuMetrics(string Name, int Utilization, double VramUsedMb, double VramTotalMb);
public sealed record SystemMetricsSnapshot(double CpuPercent, double MemUsedMb, double MemTotalMb, double MemPercent, GpuMetrics? Gpu);

/// <summary>跨平台系统资源占用采样（CPU/内存/GPU/显存），供训练页 1 秒刷新展示。</summary>
public sealed class SystemMetrics : IDisposable
{
    private static readonly TimeSpan GpuSampleInterval = TimeSpan.FromSeconds(2);

    /// <summary>nvidia-smi 的候选路径：Linux 服务常以精简 PATH 运行，只有 PATH 查找会拿不到 GPU 指标。</summary>
    private static readonly IReadOnlyList<string> GpuExecutables = NvidiaSmi.CandidateExecutables(
        OperatingSystem.IsWindows() ? OsKind.Windows : OperatingSystem.IsMacOS() ? OsKind.Mac : OsKind.Linux);

    /// <summary>单次 nvidia-smi 采样的超时：驱动异常时它可能一直不返回，不能让指标轮询被拖死。</summary>
    private static readonly TimeSpan GpuProbeTimeout = TimeSpan.FromSeconds(5);

    /// <summary>采样失败后的退避时长，避免每 2 秒重复拉起无响应的 nvidia-smi。</summary>
    private static readonly TimeSpan GpuFailureBackoff = TimeSpan.FromSeconds(60);
    private readonly object _cpuSync = new();
    private readonly SemaphoreSlim _gpuSampleLock = new(1, 1);
    private double _prevCpuIdle, _prevCpuTotal;
    private bool _hasCpuPrev;
    private GpuMetrics? _cachedGpu;
    private long _nextGpuSampleAt;

    public async ValueTask<SystemMetricsSnapshot> SampleAsync(CancellationToken cancellationToken = default)
    {
        double cpu;
        lock (_cpuSync) { cpu = SampleCpu(); }
        var (used, total) = SampleMemory();
        var gpu = await SampleGpuAsync(cancellationToken);
        var memPercent = total > 0 ? used * 100.0 / total : 0;
        return new SystemMetricsSnapshot(cpu, used, total, memPercent, gpu);
    }

    // ── CPU%（系统占用） ──
    private double SampleCpu()
    {
        try
        {
            if (OperatingSystem.IsLinux()) { return CpuFromProcStat(); }
            if (OperatingSystem.IsWindows()) { return CpuFromGetSystemTimes(); }
        }
        catch { }
        return 0;
    }

    private double CpuFromProcStat()
    {
        var line = File.ReadLines("/proc/stat").FirstOrDefault(l => l.StartsWith("cpu "));
        if (line is null) return 0;
        var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries).Skip(1).Select(p => long.TryParse(p, out var v) ? v : 0).ToArray();
        if (parts.Length < 4) return 0;
        long idle = parts[3] + (parts.Length > 4 ? parts[4] : 0);
        long total = parts.Sum();
        return DeltaCpu(idle, total);
    }

    private double CpuFromGetSystemTimes()
    {
        if (GetSystemTimes(out var idle, out var kernel, out var user))
        {
            long idleTicks = ToTicks(idle);
            long totalTicks = ToTicks(kernel) + ToTicks(user);
            return DeltaCpu(idleTicks, totalTicks);
        }
        return 0;
    }

    private double DeltaCpu(long idle, long total)
    {
        if (_hasCpuPrev)
        {
            var idleDelta = idle - _prevCpuIdle;
            var totalDelta = total - _prevCpuTotal;
            _prevCpuIdle = idle; _prevCpuTotal = total;
            if (totalDelta > 0) { var busy = totalDelta - idleDelta; return Math.Clamp(busy * 100.0 / totalDelta, 0, 100); }
            return 0;
        }
        _prevCpuIdle = idle; _prevCpuTotal = total; _hasCpuPrev = true;
        return 0;
    }

    // ── 内存 ──
    private (double used, double total) SampleMemory()
    {
        try
        {
            if (OperatingSystem.IsLinux()) { var r = MemFromProcMeminfo(); if (r.total > 0) return r; }
            if (OperatingSystem.IsWindows())
            {
                var status = new MEMORYSTATUSEX();
                if (GlobalMemoryStatusEx(status))
                {
                    double total = status.ullTotalPhys / 1048576.0;
                    double avail = status.ullAvailPhys / 1048576.0;
                    return (total - avail, total);
                }
            }
        }
        catch { }
        return (0, 0);
    }

    private (double used, double total) MemFromProcMeminfo()
    {
        var mem = new Dictionary<string, double>();
        foreach (var l in File.ReadLines("/proc/meminfo"))
        {
            var s = l.Split(':', 2);
            if (s.Length == 2) { var k = s[0].Trim(); var v = s[1].Trim().Split(' ')[0]; if (double.TryParse(v, out var val)) mem[k] = val; }
        }
        double totalKb = mem.TryGetValue("MemTotal", out var t) ? t : 0;
        double availKb = mem.TryGetValue("MemAvailable", out var a) ? a : mem.TryGetValue("MemFree", out var f) ? f : 0;
        var usedKb = totalKb - availKb;
        return (usedKb / 1024.0, totalKb / 1024.0);
    }

    // ── GPU / 显存 ──
    private async ValueTask<GpuMetrics?> SampleGpuAsync(CancellationToken cancellationToken)
    {
        var now = Environment.TickCount64;
        if (now < Volatile.Read(ref _nextGpuSampleAt)) { return _cachedGpu; }

        await _gpuSampleLock.WaitAsync(cancellationToken);
        try
        {
            now = Environment.TickCount64;
            if (now < Volatile.Read(ref _nextGpuSampleAt)) { return _cachedGpu; }

            string? output = null;
            var timedOut = false;
            foreach (var executable in GpuExecutables)
            {
                var (candidateCode, candidateOutput, _) = await TrainingShell.RunAsync(
                    executable,
                    new[] { "--query-gpu=name,utilization.gpu,memory.used,memory.total", "--format=csv,noheader,nounits" },
                    GpuProbeTimeout,
                    cancellationToken);
                if (candidateCode == TrainingShell.TimeoutExitCode) { timedOut = true; break; }
                if (candidateCode == 0 && !string.IsNullOrWhiteSpace(candidateOutput)) { output = candidateOutput; break; }
            }
            // nvidia-smi 卡住时不要每 2 秒再拉一次：退避一分钟，避免堆起一堆无响应的进程。
            var nextInterval = output is null && timedOut ? GpuFailureBackoff : GpuSampleInterval;
            Volatile.Write(ref _nextGpuSampleAt, now + (long)nextInterval.TotalMilliseconds);
            if (output is null) { return _cachedGpu; }
            var line = output.Split('\n', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
            if (line is null) return _cachedGpu;
            var p = line.Split(',').Select(x => x.Trim()).ToArray();
            if (p.Length < 4) return _cachedGpu;
            var name = p[0];
            var util = int.TryParse(p[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var u) ? u : 0;
            var used = double.TryParse(p[2], NumberStyles.Float, CultureInfo.InvariantCulture, out var um) ? um : 0;
            var total = double.TryParse(p[3], NumberStyles.Float, CultureInfo.InvariantCulture, out var tm) ? tm : 0;
            _cachedGpu = new GpuMetrics(name, util, used, total);
            return _cachedGpu;
        }
        catch (OperationCanceledException) { throw; }
        catch { return _cachedGpu; }
        finally { _gpuSampleLock.Release(); }
    }

    // Windows P/Invoke
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private class MEMORYSTATUSEX { public uint dwLength = (uint)Marshal.SizeOf<MEMORYSTATUSEX>(); public uint dwMemoryLoad; public ulong ullTotalPhys; public ulong ullAvailPhys; public ulong ullTotalPageFile; public ulong ullAvailPageFile; public ulong ullTotalVirtual; public ulong ullAvailVirtual; public ulong ullAvailExtendedVirtual; }
    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Auto)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalMemoryStatusEx([In, Out] MEMORYSTATUSEX lpBuffer);

    [DllImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetSystemTimes(out System.Runtime.InteropServices.ComTypes.FILETIME lpIdleTime, out System.Runtime.InteropServices.ComTypes.FILETIME lpKernelTime, out System.Runtime.InteropServices.ComTypes.FILETIME lpUserTime);

    private static long ToTicks(System.Runtime.InteropServices.ComTypes.FILETIME ft) => ((long)ft.dwHighDateTime << 32) | (uint)ft.dwLowDateTime;

    /// <summary>释放 GPU 采样同步资源。</summary>
    public void Dispose() => _gpuSampleLock.Dispose();
}
