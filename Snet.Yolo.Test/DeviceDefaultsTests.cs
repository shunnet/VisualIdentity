using Snet.Yolo.Server.inference;
using Snet.Yolo.Tasks.Core.Training;
using Xunit;

namespace Snet.Yolo.Test;

/// <summary>设置页的全站运算硬件必须同步为 SAM、YOLO 与 Anomalib 训练弹窗的默认设备。</summary>
public sealed class DeviceDefaultsTests
{
    private static readonly OnnxRuntimeSettings Gpu1 = new() { Hardware = OnnxRuntimeHardware.Cuda, GpuId = 1 };
    private static readonly OnnxRuntimeSettings Cpu = new() { Hardware = OnnxRuntimeHardware.Cpu, GpuId = 1 };

    /// <summary>设置为 GPU 时，训练弹窗默认选中设置页的那张 GPU。</summary>
    [Fact]
    public void TrainingDialogs_UseSettingsGpu()
    {
        Assert.Equal("1", DeviceDefaults.Yolo(Gpu1, [0, 1]));
        Assert.Equal("cuda:1", DeviceDefaults.Anomalib(Gpu1, [0, 1]));
    }

    /// <summary>设置为 CPU 时，训练弹窗默认自动，且忽略 CPU 模式下保留的 GPU 编号。</summary>
    [Fact]
    public void TrainingDialogs_UseAutoWhenSettingsIsCpu()
    {
        Assert.Equal("auto", DeviceDefaults.Yolo(Cpu, [0, 1]));
        Assert.Equal("auto", DeviceDefaults.Anomalib(Cpu, [0, 1]));
    }

    /// <summary>设置的 GPU 已不在可见列表时回退到第一张可见 GPU；完全没有 GPU 时为自动。</summary>
    [Fact]
    public void TrainingDialogs_FallBackWhenConfiguredGpuMissing()
    {
        Assert.Equal("0", DeviceDefaults.Yolo(Gpu1, [0]));
        Assert.Equal("cuda:0", DeviceDefaults.Anomalib(Gpu1, [0]));
        Assert.Equal("auto", DeviceDefaults.Yolo(Gpu1, []));
        Assert.Equal("auto", DeviceDefaults.Anomalib(Gpu1, []));
    }

    /// <summary>SAM 跟随设置页的 GPU；发行包不支持 CUDA、设置为 CPU 或没有 GPU 时使用 CPU。</summary>
    [Fact]
    public void Sam_FollowsSettingsGpu()
    {
        Assert.Equal(1, DeviceDefaults.Sam(Gpu1, supportsCuda: true, [0, 1]));
        Assert.Null(DeviceDefaults.Sam(Gpu1, supportsCuda: false, [0, 1]));
        Assert.Null(DeviceDefaults.Sam(Cpu, supportsCuda: true, [0, 1]));
        Assert.Null(DeviceDefaults.Sam(Gpu1, supportsCuda: true, []));
        Assert.Equal(0, DeviceDefaults.Sam(Gpu1, supportsCuda: true, [0]));
    }
}
