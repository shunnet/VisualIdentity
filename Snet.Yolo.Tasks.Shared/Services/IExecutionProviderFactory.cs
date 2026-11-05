using Snet.Yolo.Server.yolo.Models.Interfaces;

namespace Snet.Yolo.Tasks.Services;

/// <summary>按 Tasks 全站验证设备创建模型执行提供程序。</summary>
public interface IExecutionProviderFactory
{
    /// <summary>根据 ONNX 模型路径创建当前硬件对应的执行提供程序。</summary>
    /// <param name="modelPath">ONNX 模型的绝对路径。</param>
    /// <returns>由调用方负责释放的执行提供程序。</returns>
    IExecutionProvider Create(string modelPath);

    /// <summary>
    /// 硬件不可用时的中文说明；正常时返回 null，不据此静默更换设备。
    /// </summary>
    string? HardwareNotice => null;

    /// <summary>
    /// 在首次识别前检查所选硬件。CPU 无需准备；CUDA 会检查 NVIDIA 驱动，
    /// 并在必要时把 CUDA/cuDNN 运行库安装到应用私有目录。
    /// </summary>
    /// <param name="progress">环境准备进度。</param>
    /// <param name="cancellationToken">取消环境探测和安装。</param>
    /// <returns>不改变已保存设备配置的硬件检查结果。</returns>
    Task<HardwarePreparationResult> EnsureHardwareReadyAsync(Action<string>? progress = null, CancellationToken cancellationToken = default)
        => Task.FromResult(HardwarePreparationResult.Cpu);
}
