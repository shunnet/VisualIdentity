using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;

namespace Snet.Yolo.Tasks.Services;

public sealed record GpuMetrics(string Name, int Utilization, double VramUsedMb, double VramTotalMb);
public sealed record SystemMetricsSnapshot(double CpuPercent, double MemUsedMb, double MemTotalMb, double MemPercent, GpuMetrics? Gpu);

/// <summary>跨平台系统资源占用采样（CPU/内存/GPU/显存），供训练页 1 秒刷新展示。</summary>
public sealed class SystemMetrics
{
    private double _prevCpuIdle, _prevCpuTotal;
    private bool _hasCpuPrev;

    public SystemMetricsSnapshot Sample()
    {
        var cpu = SampleCpu();
        var (used, total) = SampleMemory();
        var gpu = SampleGpu();
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
    private GpuMetrics? SampleGpu()
    {
        try
        {
            var (code, output, err) = TrainingShell.RunAsync("nvidia-smi", "--query-gpu=name,utilization.gpu,memory.used,memory.total --format=csv,noheader,nounits", default).GetAwaiter().GetResult();
            if (code != 0 || string.IsNullOrWhiteSpace(output)) { return null; }
            var line = output.Split('\n', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
            if (line is null) return null;
            var p = line.Split(',').Select(x => x.Trim()).ToArray();
            if (p.Length < 4) return null;
            var name = p[0];
            int util = int.TryParse(p[1], out var u) ? u : 0;
            double used = double.TryParse(p[2], out var um) ? um : 0;
            double total = double.TryParse(p[3], out var tm) ? tm : 0;
            return new GpuMetrics(name, util, used, total);
        }
        catch { return null; }
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
}
