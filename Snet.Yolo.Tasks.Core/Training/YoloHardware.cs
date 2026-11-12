namespace Snet.Yolo.Tasks.Core.Training;

/// <summary>训练设备的能力：给 <see cref="YoloTrainingAdvisor"/> 限定模型规格的上限。</summary>
/// <param name="VramMb">显卡显存（MB）；多卡训练取最小的一张；0 表示未知。</param>
/// <param name="IsCpu">是否用 CPU 训练。</param>
public readonly record struct YoloHardware(int VramMb, bool IsCpu)
{
    /// <summary>未知：没有检测到显卡信息（自动选择设备、Apple 芯片等），不按硬件限制模型规格。</summary>
    public static YoloHardware Unknown => default;

    /// <summary>用 CPU 训练。</summary>
    public static YoloHardware Cpu => new(0, true);

    /// <summary>用显存为 <paramref name="vramMb"/> MB 的显卡训练。</summary>
    /// <param name="vramMb">显存（MB）。</param>
    public static YoloHardware Gpu(int vramMb) => new(Math.Max(0, vramMb), false);
}
