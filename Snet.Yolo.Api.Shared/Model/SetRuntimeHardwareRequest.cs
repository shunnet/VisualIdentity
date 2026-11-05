using Snet.Yolo.Server.inference;

namespace Snet.Yolo.Api.Model;

/// <summary>全局推理硬件设置请求，必须明确指定执行类型。</summary>
public sealed class SetRuntimeHardwareRequest
{
    /// <summary>执行硬件：Cpu 或 Cuda；Cuda 使用指定的 NVIDIA GPU。</summary>
    public required OnnxRuntimeHardware Hardware { get; init; }
    /// <summary>GPU 编号，从 0 开始；CPU 模式下保留编号但不启用 GPU。</summary>
    public int GpuId { get; init; }
}
