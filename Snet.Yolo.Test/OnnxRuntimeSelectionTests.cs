using Snet.Yolo.Server.inference;
using Snet.Yolo.Tasks.Services;
using Xunit;

namespace Snet.Yolo.Test;

/// <summary>设备设置的服务端权限、CPU 无 CUDA 副作用及不可变会话快照测试。</summary>
public sealed class OnnxRuntimeSelectionTests
{
    /// <summary>普通用户即使绕过页面按钮直接调用服务，也不能修改服务器设备。</summary>
    [Fact]
    public async Task Save_RejectsNonAdministrator()
    {
        var store = new OnnxRuntimeSettingsStore(Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".json"));
        var service = new OnnxRuntimeSettingsService(new CurrentUserContext(new RuntimeSettingsAuthentication("User")), store, null!);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.SaveAsync(new(), cancellationToken: TestContext.Current.CancellationToken));
        Assert.Equal(OnnxRuntimeHardware.Cpu, store.Current.Hardware);
    }

    /// <summary>CPU 应用和准备不能访问 CUDA 安装服务；独立 SAM CPU 也不读取全站 CUDA 选择。</summary>
    [Fact]
    public async Task CpuSelection_DoesNotPrepareCudaAndSamRemainsIndependent()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".json");
        try
        {
            var store = new OnnxRuntimeSettingsStore(path);
            var service = new OnnxRuntimeSettingsService(new CurrentUserContext(new RuntimeSettingsAuthentication("Admin")), store, null!);
            await service.SaveAsync(new(), cancellationToken: TestContext.Current.CancellationToken);
            IExecutionProviderFactory providers = new ExecutionProviderFactory(store, null!);
            Assert.Equal(HardwarePreparationResult.Cpu, await providers.EnsureHardwareReadyAsync(cancellationToken: TestContext.Current.CancellationToken));
            Assert.Null(providers.HardwareNotice);
            store.Save(new() { Hardware = OnnxRuntimeHardware.Cuda, GpuId = 4 });
            using var samOptions = new AnomalibSessionOptionsFactory(store).CreateSam(null);
            Assert.True(samOptions.EnableCpuMemArena);
        }
        finally { File.Delete(path); }
    }

    /// <summary>改变全站选择不能修改已经捕获的会话缓存标识。</summary>
    [Fact]
    public void Capture_PreservesOriginalHardwareWhenSettingsChange()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".json");
        try
        {
            var store = new OnnxRuntimeSettingsStore(path);
            var factory = new AnomalibSessionOptionsFactory(store);
            var cpu = factory.Capture();
            store.Save(new() { Hardware = OnnxRuntimeHardware.Cuda, GpuId = 3 });
            var gpu = factory.Capture();
            store.Save(new());
            Assert.Equal("cpu", cpu.CacheKey);
            Assert.Equal("cuda:3", gpu.CacheKey);
            Assert.Equal("cpu", factory.Capture().CacheKey);
        }
        finally { File.Delete(path); }
    }
}
