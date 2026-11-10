namespace Snet.Yolo.Tasks.Services;

public sealed record SystemMetricsSnapshot(double CpuPercent, double MemUsedMb, double MemTotalMb, double MemPercent, IReadOnlyList<GpuMetrics> Gpus)
{
    public GpuMetrics? Gpu => Gpus.FirstOrDefault();
}
