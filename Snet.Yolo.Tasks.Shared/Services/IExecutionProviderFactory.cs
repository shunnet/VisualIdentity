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
}
