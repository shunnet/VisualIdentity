using System.Text.Json.Serialization;

namespace Snet.Yolo.Tasks.Core.Config;

/// <summary>不可变的全站验证硬件快照，保证一次会话创建使用一致的设备配置。</summary>
public sealed record OnnxRuntimeSettings
{
    /// <summary>执行硬件；首次部署默认使用 CPU。</summary>
    public OnnxRuntimeHardware Hardware { get; init; } = OnnxRuntimeHardware.Cpu;
    /// <summary>CUDA 设备编号；CPU 模式下保留选择但不启用 GPU。</summary>
    public int GpuId { get; init; }
    /// <summary>会话缓存标识；CPU 模式忽略未启用的 GPU 编号。</summary>
    [JsonIgnore]
    public string CacheKey => Hardware == OnnxRuntimeHardware.Cpu ? "cpu" : $"cuda:{GpuId}";

    /// <summary>拒绝未知硬件类型及负 GPU 编号。</summary>
    public void Validate()
    {
        if (!Enum.IsDefined(Hardware) || GpuId < 0) { throw new ArgumentException("ONNX Runtime 硬件配置无效。"); }
    }
}
