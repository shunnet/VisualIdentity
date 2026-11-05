using Snet.Yolo.Api.Services;
using Snet.Yolo.Server;
using Snet.Yolo.Server.models.data;
using Xunit;

namespace Snet.Yolo.Test;

/// <summary>硬件切换应退役旧缓存，并允许正在运行的请求安全结束。</summary>
public sealed class ApiHardwareSwitchTests
{
    /// <summary>清空缓存不能关闭服务或提前释放活动租约，后续请求须使用新会话。</summary>
    [Fact]
    public async Task Switch_RetiresSessionsWithoutInterruptingActiveRequests()
    {
        using var cache = new InferenceSessionCache();
        using var first = cache.Acquire("Runtime:1:cpu", () => new IdentityOperate());
        var previous = first.Operate;
        cache.InvalidateAll();
        using var second = cache.Acquire("Runtime:1:cpu", () => new IdentityOperate());
        Assert.NotSame(previous, second.Operate);
        await previous.RunAsync(new ObjectDetectionData(), TestContext.Current.CancellationToken);
        first.Dispose();
        await Assert.ThrowsAsync<ObjectDisposedException>(() => previous.RunAsync(new ObjectDetectionData(), TestContext.Current.CancellationToken));
    }

    /// <summary>切换前捕获配置但延迟加载的请求不能重新填充新硬件缓存。</summary>
    [Fact]
    public async Task OldGeneration_UsesTransientSessionAndReleasesIt()
    {
        using var cache = new InferenceSessionCache();
        var oldGeneration = cache.Generation;
        cache.InvalidateAll();
        using var old = cache.Acquire("Runtime:1:cpu", () => new IdentityOperate(), oldGeneration);
        using var current = cache.Acquire("Runtime:1:cpu", () => new IdentityOperate(), cache.Generation);
        using var reused = cache.Acquire("Runtime:1:cpu", () => throw new InvalidOperationException(), cache.Generation);
        Assert.NotSame(old.Operate, current.Operate);
        Assert.Same(current.Operate, reused.Operate);
        var transient = old.Operate;
        old.Dispose();
        await Assert.ThrowsAsync<ObjectDisposedException>(() => transient.RunAsync(new ObjectDetectionData(), TestContext.Current.CancellationToken));
    }
}
