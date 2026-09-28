using Microsoft.ML.OnnxRuntime;
using Snet.Yolo.Api.Handler;
using Snet.Yolo.Api.Model;
using Snet.Yolo.Api.Services;
using Snet.Yolo.Server;
using Snet.Yolo.Server.anomalib;
using Snet.Yolo.Server.models.data;
using Snet.Yolo.Tasks.Core.Anomalib;
using Xunit;

namespace Snet.Yolo.Test;

public sealed class ReviewFixRegressionTests
{
    [Theory]
    [InlineData("user.", "user")]
    [InlineData("con.txt", "con")]
    [InlineData("aux.", "aux")]
    [InlineData("...", "user")]
    public void OwnerStoragePath_AvoidsWindowsAliases(string owner, string other)
    {
        var segment = OwnerStoragePath.Segment(owner);
        Assert.NotEqual(OwnerStoragePath.Segment(other), segment);
        Assert.False(segment.EndsWith('.'));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task HistoryCleanup_IgnoresNonPositiveRetention(int retentionDays)
    {
        var root = Path.Combine(Path.GetTempPath(), "snet-retention-" + Guid.NewGuid().ToString("N"));
        var today = Path.Combine(root, DateTime.Today.ToString("yyyy-MM-dd"));
        Directory.CreateDirectory(today);
        try
        {
            using var handler = new HistoryFileHandler(root);
            handler.SetConfig(new ConfigModel { RetentionDays = retentionDays });
            using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));
            await handler.DeleteLogicAsync(cancellation.Token);
            Assert.True(Directory.Exists(today));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public async Task HistoryCleanup_StillDeletesExpiredFolders()
    {
        var root = Path.Combine(Path.GetTempPath(), "snet-retention-" + Guid.NewGuid().ToString("N"));
        var today = Path.Combine(root, DateTime.Today.ToString("yyyy-MM-dd"));
        var expired = Path.Combine(root, DateTime.Today.AddDays(-5).ToString("yyyy-MM-dd"));
        Directory.CreateDirectory(today);
        Directory.CreateDirectory(expired);
        try
        {
            using var handler = new HistoryFileHandler(root);
            handler.SetConfig(new ConfigModel { RetentionDays = 1 });
            using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));
            await handler.DeleteLogicAsync(cancellation.Token);
            Assert.True(Directory.Exists(today));
            Assert.False(Directory.Exists(expired));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public void AnomalibScriptMaterialization_IsSafeForConcurrentRuns()
    {
        var root = Path.Combine(Path.GetTempPath(), "snet-script-" + Guid.NewGuid().ToString("N"));
        try
        {
            Parallel.For(0, 16, _ => AnomalibPythonPipeline.Materialize(root));
            Assert.Equal(AnomalibPythonPipeline.Source, File.ReadAllText(Path.Combine(root, AnomalibPythonPipeline.FileName)));
            Assert.Empty(Directory.EnumerateFiles(root, "*.tmp"));
        }
        finally { if (Directory.Exists(root)) { Directory.Delete(root, recursive: true); } }
    }

    [Fact]
    public async Task InferenceCache_DefersInvalidationUntilLeaseEnds()
    {
        using var cache = new InferenceSessionCache();
        using var first = cache.Acquire("cpu:7:model", () => new IdentityOperate());
        var firstOperate = first.Operate;

        cache.Invalidate("cpu", 7);
        using var second = cache.Acquire("cpu:7:model", () => new IdentityOperate());
        Assert.NotSame(firstOperate, second.Operate);

        first.Dispose();
        await Assert.ThrowsAsync<ObjectDisposedException>(() => firstOperate.RunAsync(new ObjectDetectionData(), CancellationToken.None));
        cache.Dispose();
        Assert.Throws<ObjectDisposedException>(() => cache.Acquire("cpu:7:model", () => new IdentityOperate()));
    }

    [Fact]
    public async Task InferenceCache_DisposesSessionCreatedDuringShutdownAfterLeaseEnds()
    {
        using var cache = new InferenceSessionCache();
        using var started = new ManualResetEventSlim();
        using var continueFactory = new ManualResetEventSlim();
        var acquisition = Task.Run(() => cache.Acquire("cpu:8:model", () =>
        {
            started.Set();
            if (!continueFactory.Wait(TimeSpan.FromSeconds(5))) { throw new TimeoutException(); }
            return new IdentityOperate();
        }));

        Assert.True(started.Wait(TimeSpan.FromSeconds(5)));
        cache.Dispose();
        continueFactory.Set();
        using var lease = await acquisition.WaitAsync(TimeSpan.FromSeconds(10));
        var operate = lease.Operate;
        lease.Dispose();
        await Assert.ThrowsAsync<ObjectDisposedException>(() => operate.RunAsync(new ObjectDetectionData(), CancellationToken.None));
    }

    [Fact]
    public async Task AnomalibInference_RejectsNewWorkAfterDisposal()
    {
        using var inference = new AnomalibOnnxInference(new CpuOptionsFactory());
        inference.Dispose();
        await Assert.ThrowsAsync<ObjectDisposedException>(() =>
            inference.IdentifyAsync("missing.onnx", "missing.json", "missing.png"));
    }

    private sealed class CpuOptionsFactory : IAnomalibSessionOptionsFactory
    {
        public SessionOptions Create() => new();
    }
}
