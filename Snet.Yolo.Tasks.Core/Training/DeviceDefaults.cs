namespace Snet.Yolo.Tasks.Core.Training;

using Snet.Yolo.Server.inference;

/// <summary>
/// 把设置页保存的全站运算硬件换算成各功能的默认设备：
/// 设置为 NVIDIA GPU 时，SAM、YOLO 训练弹窗和 Anomalib 训练弹窗都默认使用同一张 GPU；
/// 设置为 CPU（或没有可见 GPU）时，训练弹窗默认“自动”，SAM 使用 CPU。
/// 这些都只是默认值，用户仍可在各自界面手动改选。
/// </summary>
public static class DeviceDefaults
{
    /// <summary>YOLO 训练弹窗的默认设备参数。</summary>
    /// <param name="settings">设置页保存的全站硬件配置。</param>
    /// <param name="availableGpus">当前检测到的 GPU 编号。</param>
    /// <returns>GPU 编号（如 <c>0</c>）或 <c>auto</c>。</returns>
    public static string Yolo(OnnxRuntimeSettings settings, IReadOnlyCollection<int> availableGpus)
        => PickGpu(settings, availableGpus) is { } gpu ? gpu.ToString(System.Globalization.CultureInfo.InvariantCulture) : "auto";

    /// <summary>Anomalib 训练弹窗的默认设备参数。</summary>
    /// <param name="settings">设置页保存的全站硬件配置。</param>
    /// <param name="availableGpus">当前检测到的 GPU 编号。</param>
    /// <returns><c>cuda:N</c> 或 <c>auto</c>。</returns>
    public static string Anomalib(OnnxRuntimeSettings settings, IReadOnlyCollection<int> availableGpus)
        => PickGpu(settings, availableGpus) is { } gpu ? "cuda:" + gpu.ToString(System.Globalization.CultureInfo.InvariantCulture) : "auto";

    /// <summary>SAM 面板的默认设备。</summary>
    /// <param name="settings">设置页保存的全站硬件配置。</param>
    /// <param name="supportsCuda">当前发行包是否支持 CUDA 推理。</param>
    /// <param name="availableGpus">当前检测到的 GPU 编号。</param>
    /// <returns>GPU 编号；使用 CPU 时为空。</returns>
    public static int? Sam(OnnxRuntimeSettings settings, bool supportsCuda, IReadOnlyCollection<int> availableGpus)
        => supportsCuda ? PickGpu(settings, availableGpus) : null;

    /// <summary>选出默认 GPU：优先设置页选中的那张，其次是第一张可见 GPU；设置为 CPU 或没有 GPU 时为空。</summary>
    private static int? PickGpu(OnnxRuntimeSettings settings, IReadOnlyCollection<int> availableGpus)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(availableGpus);
        if (settings.Hardware != OnnxRuntimeHardware.Cuda || availableGpus.Count == 0) { return null; }
        return availableGpus.Contains(settings.GpuId) ? settings.GpuId : availableGpus.Min();
    }
}
