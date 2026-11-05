using Snet.Yolo.Server.inference;
using Xunit;

namespace Snet.Yolo.Test;

/// <summary>全站推理设备配置必须持久化、严格校验并在失败时保留旧快照。</summary>
public sealed class OnnxRuntimeSettingsTests
{
    /// <summary>隔离路径下默认 CPU，CUDA 配置保存后可在新实例中恢复。</summary>
    [Fact]
    public void Store_RoundTripsHardwareAndGpuId()
    {
        var directory = Path.Combine(Path.GetTempPath(), "onnx-settings-" + Guid.NewGuid().ToString("N"));
        var path = Path.Combine(directory, "settings.json");
        try
        {
            var store = new OnnxRuntimeSettingsStore(path);
            Assert.Equal(OnnxRuntimeHardware.Cpu, store.Current.Hardware);
            var settings = new OnnxRuntimeSettings { Hardware = OnnxRuntimeHardware.Cuda, GpuId = 3 };
            store.Save(settings);
            Assert.Equal(settings, new OnnxRuntimeSettingsStore(path).Current);
        }
        finally { if (Directory.Exists(directory)) { Directory.Delete(directory, true); } }
    }

    /// <summary>非法 GPU 和未知枚举不能改变内存快照或落盘配置。</summary>
    [Theory]
    [InlineData(0, -1)]
    [InlineData(987, 0)]
    public void Store_RejectsInvalidHardwareWithoutChangingCurrent(int hardware, int gpuId)
    {
        var store = new OnnxRuntimeSettingsStore(Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".json"));
        var before = store.Current;
        Assert.Throws<ArgumentException>(() => store.Save(new() { Hardware = (OnnxRuntimeHardware)hardware, GpuId = gpuId }));
        Assert.Same(before, store.Current);
    }

    /// <summary>原子写入失败必须保留旧配置，不提前切换运行设备。</summary>
    [Fact]
    public void Store_WriteFailurePreservesPreviousSnapshot()
    {
        var directory = Path.Combine(Path.GetTempPath(), "onnx-settings-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var store = new OnnxRuntimeSettingsStore(directory);
            var before = store.Current;
            var error = Record.Exception(() => store.Save(new() { Hardware = OnnxRuntimeHardware.Cuda, GpuId = 2 }));
            Assert.True(error is IOException or UnauthorizedAccessException);
            Assert.Same(before, store.Current);
        }
        finally { Directory.Delete(directory, true); }
    }

    /// <summary>损坏配置以 CPU 启动，避免错误配置阻断整个站点。</summary>
    [Fact]
    public void Store_CorruptFileUsesCpuDefault()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".json");
        File.WriteAllText(path, "{broken");
        try { Assert.Equal(OnnxRuntimeHardware.Cpu, new OnnxRuntimeSettingsStore(path).Current.Hardware); }
        finally { File.Delete(path); }
    }
}
