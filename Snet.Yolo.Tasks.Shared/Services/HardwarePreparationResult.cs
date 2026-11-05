namespace Snet.Yolo.Tasks.Services;

/// <summary>所选推理硬件的准备结果，不改变设备配置。</summary>
/// <param name="GpuBuild">本次是否请求 CUDA；保留原有属性名以兼容调用方。</param>
/// <param name="GpuReady">设备及运行库检查是否通过；实际模型会话仍需成功初始化。</param>
/// <param name="Installed">本次是否安装私有运行库。</param>
/// <param name="Message">环境状态或失败原因。</param>
public sealed record HardwarePreparationResult(bool GpuBuild, bool GpuReady, bool Installed, string Message)
{
    /// <summary>显式 CPU 模式无需准备 CUDA。</summary>
    public static HardwarePreparationResult Cpu { get; } = new(false, false, false, string.Empty);
}
