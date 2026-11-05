using SkiaSharp;
using Snet.Yolo.Server.inference;
using Snet.Yolo.Server.models.data;
using Snet.Yolo.Server.models.@enum;
using Snet.Yolo.Tool.Services;
using Xunit;

namespace Snet.Yolo.Test;

/// <summary>桌面硬件动态切换的原生 CPU 推理、持久化及会话退役回归。</summary>
public sealed class ToolRuntimeTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "snet-tool-runtime-" + Guid.NewGuid().ToString("N"));

    /// <summary>创建独立的模型与硬件配置目录。</summary>
    public ToolRuntimeTests() => Directory.CreateDirectory(_root);

    /// <summary>默认 CPU；保存的桌面设置在运行服务重新创建后继续生效。</summary>
    [Fact]
    public void CpuConfiguration_PersistsAcrossRuntimeInstances()
    {
        var path = Path.Combine(_root, "hardware.json");
        using (var runtime = new ToolRuntimeService(new(path)))
        {
            Assert.Equal(OnnxRuntimeHardware.Cpu, runtime.Current.Hardware);
            runtime.Configure(new() { Hardware = OnnxRuntimeHardware.Cpu, GpuId = 2 });
        }
        using var reopened = new ToolRuntimeService(new(path));
        Assert.Equal(OnnxRuntimeHardware.Cpu, reopened.Current.Hardware);
        Assert.Equal(2, reopened.Current.GpuId);
    }

    /// <summary>相同模型和有效设备复用会话；CPU 模式保存未启用的 GPU 编号不重建会话。</summary>
    [Fact]
    public void SameModelAndDevice_ReusesSession()
    {
        using var runtime = CreateRuntime();
        var model = WriteModel("one.onnx");
        using var first = runtime.Acquire(model, OnnxType.ObjectDetection);
        runtime.Configure(new() { Hardware = OnnxRuntimeHardware.Cpu, GpuId = 3 });
        using var second = runtime.Acquire(model, OnnxType.ObjectDetection);
        Assert.Same(first.Operate, second.Operate);
    }

    /// <summary>无法使用目标 GPU 时不保存、不退役当前 CPU 会话。</summary>
    [Fact]
    public void UnavailableGpu_PreservesSavedDeviceAndSession()
    {
        using var runtime = CreateRuntime();
        runtime.Configure(new());
        var original = File.ReadAllBytes(Path.Combine(_root, "hardware.json"));
        var model = WriteModel("one.onnx");
        using var before = runtime.Acquire(model, OnnxType.ObjectDetection);
        Assert.ThrowsAny<Exception>(() => runtime.Configure(new() { Hardware = OnnxRuntimeHardware.Cuda, GpuId = int.MaxValue }));
        Assert.Equal(OnnxRuntimeHardware.Cpu, runtime.Current.Hardware);
        Assert.Equal(original, File.ReadAllBytes(Path.Combine(_root, "hardware.json")));
        using var after = runtime.Acquire(model, OnnxType.ObjectDetection);
        Assert.Same(before.Operate, after.Operate);
    }

    /// <summary>替换模型后，旧会话必须等两个使用者都归还后再释放。</summary>
    [Fact]
    public async Task ReplacedModel_RetiresAfterLastLease()
    {
        using var runtime = CreateRuntime();
        var model = WriteModel("one.onnx");
        using var first = runtime.Acquire(model, OnnxType.ObjectDetection);
        using var second = runtime.Acquire(model, OnnxType.ObjectDetection);
        using var replacement = runtime.Acquire(WriteModel("two.onnx"), OnnxType.ObjectDetection);
        Assert.NotSame(first.Operate, replacement.Operate);
        first.Dispose();
        Assert.True((await second.Operate.RunAsync(Image(), TestContext.Current.CancellationToken)).Status);
        second.Dispose();
        second.Dispose(); // 重复归还不能使引用计数为负数。
        await Assert.ThrowsAsync<ObjectDisposedException>(() => second.Operate.RunAsync(Image(), TestContext.Current.CancellationToken));
        Assert.True((await replacement.Operate.RunAsync(Image(), TestContext.Current.CancellationToken)).Status);
    }

    /// <summary>运行服务关闭时保留活动会话；归还后拒绝继续使用。</summary>
    [Fact]
    public async Task Shutdown_KeepsActiveLeaseAlive()
    {
        using var runtime = CreateRuntime();
        using var lease = runtime.Acquire(WriteModel("one.onnx"), OnnxType.ObjectDetection);
        runtime.Dispose();
        runtime.Dispose();
        Assert.True((await lease.Operate.RunAsync(Image(), TestContext.Current.CancellationToken)).Status);
        Assert.Throws<ObjectDisposedException>(() => runtime.Acquire(WriteModel("two.onnx"), OnnxType.ObjectDetection));
        Assert.Throws<ObjectDisposedException>(() => runtime.Configure(new()));
        lease.Dispose();
        await Assert.ThrowsAsync<ObjectDisposedException>(() => lease.Operate.RunAsync(Image(), TestContext.Current.CancellationToken));
    }

    /// <summary>新模型加载失败时，原来的可用会话继续复用。</summary>
    [Fact]
    public void InvalidReplacement_DoesNotEvictWorkingModel()
    {
        using var runtime = CreateRuntime();
        var model = WriteModel("one.onnx");
        using var first = runtime.Acquire(model, OnnxType.ObjectDetection);
        var invalid = Path.Combine(_root, "invalid.onnx");
        File.WriteAllBytes(invalid, [1, 2, 3]);
        Assert.ThrowsAny<Exception>(() => runtime.Acquire(invalid, OnnxType.ObjectDetection));
        using var second = runtime.Acquire(model, OnnxType.ObjectDetection);
        Assert.Same(first.Operate, second.Operate);
    }

    /// <summary>配置目录不可写时不改变当前硬件及可用会话。</summary>
    [Fact]
    public void SaveFailure_DoesNotEvictWorkingModel()
    {
        var blocked = Path.Combine(_root, "blocked");
        File.WriteAllText(blocked, "a file cannot be a directory");
        using var runtime = new ToolRuntimeService(new(Path.Combine(blocked, "hardware.json")));
        var model = WriteModel("one.onnx");
        using var first = runtime.Acquire(model, OnnxType.ObjectDetection);
        Assert.ThrowsAny<IOException>(() => runtime.Configure(new() { GpuId = 2 }));
        Assert.Equal(0, runtime.Current.GpuId);
        using var second = runtime.Acquire(model, OnnxType.ObjectDetection);
        Assert.Same(first.Operate, second.Operate);
    }

    /// <summary>并发识别复用一个原生会话，并由 Server 串行访问模型。</summary>
    [Fact]
    public async Task ConcurrentInference_ReusesNativeSession()
    {
        using var runtime = CreateRuntime();
        var model = WriteModel("one.onnx");
        var leases = await Task.WhenAll(Enumerable.Range(0, 6).Select(_ => Task.Run(
            () => runtime.Acquire(model, OnnxType.ObjectDetection), TestContext.Current.CancellationToken)));
        try
        {
            Assert.All(leases, lease => Assert.Same(leases[0].Operate, lease.Operate));
            var results = await Task.WhenAll(leases.Select(lease => Task.Run(
                () => lease.Operate.RunAsync(Image(), TestContext.Current.CancellationToken), TestContext.Current.CancellationToken)));
            Assert.All(results, result => Assert.True(result.Status));
        }
        finally { foreach (var lease in leases) { lease.Dispose(); } }
    }

    /// <summary>创建使用当前独立配置文件的运行服务。</summary>
    private ToolRuntimeService CreateRuntime() => new(new(Path.Combine(_root, "hardware.json")));

    /// <summary>写入无需下载权重的有效 YOLO 测试模型。</summary>
    /// <param name="name">临时文件名。</param>
    /// <returns>模型绝对路径。</returns>
    private string WriteModel(string name)
    {
        var path = Path.Combine(_root, name);
        File.WriteAllBytes(path, ExecutionProviderFixtures.Float);
        return path;
    }

    /// <summary>创建用于真实 ONNX Runtime 推理的图片和检测参数。</summary>
    private static ObjectDetectionData Image()
    {
        using var bitmap = new SKBitmap(4, 4);
        bitmap.Erase(SKColors.Gray);
        using var encoded = bitmap.Encode(SKEncodedImageFormat.Png, 100);
        return new() { File = encoded.ToArray() };
    }

    /// <summary>服务及租约释放后删除临时文件。</summary>
    public void Dispose() => Directory.Delete(_root, recursive: true);
}
