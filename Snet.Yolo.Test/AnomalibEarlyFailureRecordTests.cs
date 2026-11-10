using Snet.Yolo.Server.anomalib;
using Snet.Yolo.Server.models;
using Snet.Yolo.Tasks.Core.History;
using Snet.Yolo.Tasks.Services;
using Xunit;

namespace Snet.Yolo.Test;

/// <summary>训练在创建运行目录之前就失败或被取消时，也必须留下一条训练记录。</summary>
public sealed class AnomalibEarlyFailureRecordTests : IDisposable
{
    /// <summary>每个测试独立的临时目录。</summary>
    private readonly string _root = Path.Combine(Path.GetTempPath(), "early-failure-" + Guid.NewGuid().ToString("N"));

    /// <summary>创建目录。</summary>
    public AnomalibEarlyFailureRecordTests() => Directory.CreateDirectory(_root);

    /// <summary>删除临时目录。</summary>
    public void Dispose() => Directory.Delete(_root, recursive: true);

    /// <summary>构造训练请求：图片内容完全相同，去重后不足 10 张，规划数据集时会失败。</summary>
    private AnomalibTrainingRequest DuplicateImagesRequest()
    {
        var images = Enumerable.Range(0, 3).Select(index =>
        {
            var path = Path.Combine(_root, $"same-{index}.png");
            File.WriteAllBytes(path, [1, 2, 3, 4]);
            return path;
        }).ToList();
        return new AnomalibTrainingRequest
        {
            Owner = "snet", ProjectId = "p1", WorkingDirectory = Path.Combine(_root, "work"), PythonExecutable = "python",
            Images = images, Options = new AnomalibTrainingOptions(),
        };
    }

    /// <summary>上传了大量重复图片：数据集规划失败时没有运行目录，但训练指标页仍要看到这次失败。</summary>
    [Fact]
    public async Task DuplicateImages_FailBeforeTheRunDirectoryExists_ButStillLeaveARecord()
    {
        var store = new TrainingHistoryStore(Path.Combine(_root, "train"));
        var service = new AnomalibTrainingService(new UnusedAnomalibProcessRunner(), new UnusedAnomalibModelRegistrar(), store);

        var result = await service.TrainAsync(DuplicateImagesRequest(), null, CancellationToken.None);

        Assert.False(result.Succeeded);
        var record = Assert.Single(await store.ListAsync(ProjectKind.Anomalib, "snet", "p1"));
        Assert.Equal(TrainingRunStatus.Failed, record.Status);
        Assert.False(record.HasModel);
        Assert.Null(record.Anomalib);
        Assert.False(string.IsNullOrWhiteSpace(record.Message));
    }

    /// <summary>在数据集规划阶段被取消：同样留下一条“已取消”的记录，且取消异常照常抛出。</summary>
    [Fact]
    public async Task CancelledBeforeTheRunDirectoryExists_LeavesACancelledRecord()
    {
        var store = new TrainingHistoryStore(Path.Combine(_root, "train"));
        var service = new AnomalibTrainingService(new UnusedAnomalibProcessRunner(), new UnusedAnomalibModelRegistrar(), store);
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.TrainAsync(DuplicateImagesRequest(), null, cancellation.Token));

        var record = Assert.Single(await store.ListAsync(ProjectKind.Anomalib, "snet", "p1"));
        Assert.Equal(TrainingRunStatus.Cancelled, record.Status);
    }
}
