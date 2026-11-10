namespace Snet.Yolo.Tasks.Core.Training;

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
