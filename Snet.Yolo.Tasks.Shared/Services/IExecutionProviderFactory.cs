using YoloDotNet.Models.Interfaces;

namespace Snet.Yolo.Tasks.Services;

/// <summary>为当前 Tasks 硬件版本创建模型执行提供程序。</summary>
public interface IExecutionProviderFactory
{
    /// <summary>根据 ONNX 模型路径创建当前硬件对应的执行提供程序。</summary>
    /// <param name="modelPath">ONNX 模型的绝对路径。</param>
    /// <returns>由调用方负责释放的执行提供程序。</returns>
    IExecutionProvider Create(string modelPath);

    /// <summary>
    /// 硬件加速不可用时的中文说明（已自动降级到 CPU）；正常时返回 null。
    /// 默认实现返回 null，只有需要降级的硬件版本才覆盖它。
    /// </summary>
    string? HardwareNotice => null;

    /// <summary>
    /// 在首次识别前检查并准备当前硬件后端。CPU 版本无需准备；GPU 版本会检查 NVIDIA 驱动，
    /// 并在必要时把 CUDA/cuDNN 运行库安装到应用私有目录。
    /// </summary>
    Task<HardwarePreparationResult> EnsureHardwareReadyAsync(Action<string>? progress = null, CancellationToken cancellationToken = default)
        => Task.FromResult(HardwarePreparationResult.Cpu);
}

/// <summary>硬件后端准备结果。</summary>
/// <param name="GpuBuild">当前是否为 CUDA GPU 构建。</param>
/// <param name="GpuReady">CUDA 是否可用于本次推理。</param>
/// <param name="Installed">本次是否下载安装了运行库。</param>
/// <param name="Message">面向用户的结果说明。</param>
public sealed record HardwarePreparationResult(bool GpuBuild, bool GpuReady, bool Installed, string Message)
{
    /// <summary>CPU 构建无需 CUDA 准备。</summary>
    public static HardwarePreparationResult Cpu { get; } = new(false, false, false, string.Empty);
}
