namespace Snet.Yolo.Tasks.Services;

public sealed record GpuMetrics(int Index, string Name, int Utilization, double VramUsedMb, double VramTotalMb);
