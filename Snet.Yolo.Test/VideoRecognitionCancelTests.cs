using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Snet.Yolo.Server.models.data;
using Snet.Yolo.Tasks.Services;
using Xunit;

namespace Snet.Yolo.Test;

/// <summary>
/// 异步任务的取消语义（视频识别部分）：
/// 排队中的任务可以被取消、取消后状态终结且不允许被后续进度覆盖、
/// 取消过的任务允许重新提交（否则用户取消一次就再也跑不了）。
/// </summary>
public sealed class VideoRecognitionCancelTests
{
    /// <summary>测试里不启动工作器，作用域工厂不会被用到。</summary>
    private sealed class UnusedScopeFactory : IServiceScopeFactory
    {
        public IServiceScope CreateScope() => throw new NotSupportedException("测试不启动工作器。");
    }

    private static (VideoRecognitionQueue Queue, OnnxData Model, ValidationImageState Image) NewQueue()
    {
        var queue = new VideoRecognitionQueue(new UnusedScopeFactory(), new ValidationState(), NullLogger<VideoRecognitionQueue>.Instance);
        var model = new OnnxData { index = 3, name = "m.onnx" };
        var image = new ValidationImageState(Guid.NewGuid(), "v.mp4", "/uploads/val/v.mp4", null, Array.Empty<ValidationDetection>(), IsVideo: true);
        return (queue, model, image);
    }

    [Fact]
    public void CancellingQueuedJob_MarksItTerminalCancelled()
    {
        var (queue, model, image) = NewQueue();
        Assert.True(queue.TryEnqueue("snet", model, image, "{}"));

        Assert.True(queue.TryCancel("snet", model.index, image.Id));

        var status = queue.GetStatus("snet", model.index, image.Id);
        Assert.NotNull(status);
        Assert.Equal(VideoRecognitionStage.Cancelled, status!.Stage);
        Assert.True(status.IsTerminal);
        Assert.Null(status.Error);
    }

    [Fact]
    public void CancellingFinishedOrUnknownJob_ReturnsFalse()
    {
        var (queue, model, image) = NewQueue();

        Assert.False(queue.TryCancel("snet", model.index, image.Id));       // 任务不存在

        Assert.True(queue.TryEnqueue("snet", model, image, "{}"));
        Assert.True(queue.TryCancel("snet", model.index, image.Id));
        Assert.False(queue.TryCancel("snet", model.index, image.Id));       // 已经取消过
    }

    [Fact]
    public void CancelledJob_CanBeRequeued()
    {
        var (queue, model, image) = NewQueue();
        Assert.True(queue.TryEnqueue("snet", model, image, "{}"));
        Assert.True(queue.TryCancel("snet", model.index, image.Id));

        // 取消后可以重新开始识别（终态才允许替换）
        Assert.True(queue.TryEnqueue("snet", model, image, "{}"));

        var status = queue.GetStatus("snet", model.index, image.Id);
        Assert.Equal(VideoRecognitionStage.Queued, status!.Stage);
    }

    [Fact]
    public void RunningJob_IsRejectedWhileActive()
    {
        var (queue, model, image) = NewQueue();
        Assert.True(queue.TryEnqueue("snet", model, image, "{}"));

        // 未结束时重复提交会被拒绝（避免同一视频并发推理）
        Assert.False(queue.TryEnqueue("snet", model, image, "{}"));
    }

    [Fact]
    public void Forget_RemovesOnlyTerminalJobs()
    {
        var (queue, model, image) = NewQueue();
        Assert.True(queue.TryEnqueue("snet", model, image, "{}"));

        queue.Forget("snet", model.index, image.Id);   // 还在排队：不应被清掉
        Assert.NotNull(queue.GetStatus("snet", model.index, image.Id));

        Assert.True(queue.TryCancel("snet", model.index, image.Id));
        queue.Forget("snet", model.index, image.Id);   // 已终结：可以清掉
        Assert.Null(queue.GetStatus("snet", model.index, image.Id));
    }
}
