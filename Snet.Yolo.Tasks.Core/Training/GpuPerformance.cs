namespace Snet.Yolo.Tasks.Core.Training;

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
