using Snet.Yolo.Server.models.data;
using System.Collections.Concurrent;
using System.Threading.Channels;

namespace Snet.Yolo.Tasks.Services;

/// <summary>视频后台任务当前所处的处理阶段。</summary>
public enum VideoRecognitionStage
{
    /// <summary>任务正在等待后台工作器。</summary>
    Queued,
    /// <summary>正在读取视频信息并抽取帧。</summary>
    Decoding,
    /// <summary>正在加载 ONNX 模型。</summary>
    LoadingModel,
    /// <summary>正在逐帧执行推理。</summary>
    Recognizing,
    /// <summary>正在编码带标注的视频。</summary>
    Encoding,
    /// <summary>任务已经成功完成。</summary>
    Completed,
    /// <summary>任务被用户取消。</summary>
    Cancelled,
    /// <summary>任务执行失败。</summary>
    Failed,
}

/// <summary>供验证页面读取的视频任务不可变状态快照。</summary>
public sealed record VideoRecognitionStatus(
    VideoRecognitionStage Stage,
    int TotalFrames,
    int CompletedFrames,
    double FramesPerSecond,
    TimeSpan Elapsed,
    string? Error,
    double? LastFrameRunTimeMilliseconds)
{
    /// <summary>指示后台任务是否已经结束（含被用户取消）。</summary>
    public bool IsTerminal => Stage is VideoRecognitionStage.Completed or VideoRecognitionStage.Cancelled or VideoRecognitionStage.Failed;
}

/// <summary>
/// 宿主级有界视频识别队列。任务不依赖 Blazor 电路，因此页面刷新或暂时断开不会中止推理。
/// </summary>
public sealed class VideoRecognitionQueue : BackgroundService
{
    private const int QueueCapacity = 8;
    private readonly Channel<VideoRecognitionRequest> _queue = Channel.CreateBounded<VideoRecognitionRequest>(
        new BoundedChannelOptions(QueueCapacity) { FullMode = BoundedChannelFullMode.Wait, SingleReader = true, SingleWriter = false });
    private readonly ConcurrentDictionary<VideoJobKey, MutableStatus> _statuses = new();
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ValidationState _state;
    private readonly ILogger<VideoRecognitionQueue> _logger;

    /// <summary>创建串行视频工作器，避免多个大型模型同时争用同一 GPU 或内存。</summary>
    public VideoRecognitionQueue(IServiceScopeFactory scopeFactory, ValidationState state, ILogger<VideoRecognitionQueue> logger)
    {
        _scopeFactory = scopeFactory;
        _state = state;
        _logger = logger;
    }

    /// <summary>尝试提交视频任务；同一用户、模型和文件同时只允许一个活动任务。</summary>
    public bool TryEnqueue(string owner, OnnxData model, ValidationImageState image, string parameterJson)
    {
        var key = new VideoJobKey(owner, model.index, image.Id);
        var status = new MutableStatus();
        if (!_statuses.TryAdd(key, status))
        {
            return _statuses.TryGetValue(key, out var existing) && existing.IsTerminal
                ? ReplaceCompletedJob(key, existing, status, new VideoRecognitionRequest(key, model, image, parameterJson, status))
                : false;
        }

        if (_queue.Writer.TryWrite(new VideoRecognitionRequest(key, model, image, parameterJson, status))) { return true; }
        _statuses.TryRemove(key, out _);
        return false;
    }

    /// <summary>读取指定视频任务的最新进度；任务不存在时返回空。</summary>
    public VideoRecognitionStatus? GetStatus(string owner, int modelIndex, Guid imageId)
        => _statuses.TryGetValue(new VideoJobKey(owner, modelIndex, imageId), out var status) ? status.Snapshot() : null;

    /// <summary>移除已经结束且对应文件已删除的任务状态。</summary>
    public void Forget(string owner, int modelIndex, Guid imageId)
    {
        var key = new VideoJobKey(owner, modelIndex, imageId);
        if (_statuses.TryGetValue(key, out var status) && status.IsTerminal) { _statuses.TryRemove(key, out _); }
    }

    /// <summary>
    /// 取消指定视频任务：排队中的任务会被工作器跳过，正在执行的任务会中断（抽帧/推理/编码都监听令牌）。
    /// 返回 false 表示任务不存在或已经结束。
    /// </summary>
    public bool TryCancel(string owner, int modelIndex, Guid imageId)
    {
        var key = new VideoJobKey(owner, modelIndex, imageId);
        if (!_statuses.TryGetValue(key, out var status)) { return false; }
        if (!status.Cancel()) { return false; }
        _logger.LogInformation("Video recognition cancelled for {Owner}/{ModelIndex}/{ImageId}", owner, modelIndex, imageId);
        return true;
    }

    /// <summary>消费队列并将最终结果写回进程生命周期验证状态。</summary>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var request in _queue.Reader.ReadAllAsync(stoppingToken))
        {
            if (!_statuses.TryGetValue(request.Key, out var status) || !ReferenceEquals(status, request.Status)) { continue; }
            if (status.IsCancelled) { continue; }   // 排队期间已被取消
            // 应用停机与用户取消都会中断任务，两者用不同分支收尾
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken, status.Token);
            try
            {
                await using var scope = _scopeFactory.CreateAsyncScope();
                var service = scope.ServiceProvider.GetRequiredService<ValidationService>();
                var result = await service.ProcessVideoAsync(
                    request.Key.Owner,
                    request.Model,
                    request.Image,
                    request.ParameterJson,
                    status.Report,
                    linked.Token);
                _state.SetVideoResult(
                    request.Key.Owner,
                    request.Key.ModelIndex,
                    request.Key.ImageId,
                    result.ResultJson,
                    result.Detections,
                    result.ResultUrl);
                status.SetLastFrameRunTime(result.LastFrameRunTimeMilliseconds);
                status.Complete();
            }
            catch (OperationCanceledException) when (status.IsCancelled)
            {
                // 用户主动取消：状态已是 Cancelled，无需再标记失败
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                status.Fail("应用正在停止，视频识别已取消。");
            }
            catch (Exception ex)
            {
                status.Fail(ex.Message);
                _logger.LogError(ex, "Video recognition failed for {Owner}/{ModelIndex}/{ImageId}", request.Key.Owner, request.Key.ModelIndex, request.Key.ImageId);
            }
        }
    }

    /// <summary>使用新任务替换同一文件已经结束的旧状态。</summary>
    private bool ReplaceCompletedJob(VideoJobKey key, MutableStatus previousStatus, MutableStatus status, VideoRecognitionRequest request)
    {
        if (!_statuses.TryUpdate(key, status, previousStatus)) { return false; }
        if (_queue.Writer.TryWrite(request)) { return true; }
        status.Fail("视频识别队列已满，请稍后重试。");
        return false;
    }

    private sealed record VideoRecognitionRequest(
        VideoJobKey Key,
        OnnxData Model,
        ValidationImageState Image,
        string ParameterJson,
        MutableStatus Status);
    private readonly record struct VideoJobKey(string Owner, int ModelIndex, Guid ImageId);

    private sealed class MutableStatus
    {
        private readonly object _gate = new();
        private readonly System.Diagnostics.Stopwatch _stopwatch = System.Diagnostics.Stopwatch.StartNew();
        /// <summary>本任务的取消源：用户点"取消"时中断抽帧/推理/编码（无计时器，交给 GC 回收即可）。</summary>
        private readonly CancellationTokenSource _cancellation = new();
        private VideoRecognitionStage _stage = VideoRecognitionStage.Queued;
        private int _totalFrames;
        private int _completedFrames;
        private double _framesPerSecond;
        private string? _error;
        private double? _lastFrameRunTimeMilliseconds;

        /// <summary>本任务的取消令牌（与宿主停机令牌组合后传给识别流程）。</summary>
        public CancellationToken Token => _cancellation.Token;

        public bool IsTerminal { get { lock (_gate) { return _stage is VideoRecognitionStage.Completed or VideoRecognitionStage.Cancelled or VideoRecognitionStage.Failed; } } }

        public bool IsCancelled { get { lock (_gate) { return _stage == VideoRecognitionStage.Cancelled; } } }

        /// <summary>标记为已取消并中断执行；已经结束的任务返回 false。</summary>
        public bool Cancel()
        {
            lock (_gate)
            {
                if (_stage is VideoRecognitionStage.Completed or VideoRecognitionStage.Cancelled or VideoRecognitionStage.Failed) { return false; }
                _stage = VideoRecognitionStage.Cancelled;
                _error = null;
                _stopwatch.Stop();
            }
            try { _cancellation.Cancel(); } catch (ObjectDisposedException) { }
            return true;
        }

        public void Report(VideoRecognitionStage stage, int totalFrames, int completedFrames, double framesPerSecond)
        {
            lock (_gate)
            {
                // 已取消的任务不再被后续进度覆盖
                if (_stage == VideoRecognitionStage.Cancelled) { return; }
                _stage = stage;
                _totalFrames = totalFrames;
                _completedFrames = completedFrames;
                _framesPerSecond = framesPerSecond;
            }
        }

        public void Complete()
        {
            lock (_gate)
            {
                if (_stage == VideoRecognitionStage.Cancelled) { return; }
                _stage = VideoRecognitionStage.Completed;
                _completedFrames = _totalFrames;
                _stopwatch.Stop();
            }
        }

        public void SetLastFrameRunTime(double milliseconds)
        {
            lock (_gate) { _lastFrameRunTimeMilliseconds = milliseconds; }
        }

        public void Fail(string error)
        {
            lock (_gate)
            {
                if (_stage == VideoRecognitionStage.Cancelled) { return; }
                _stage = VideoRecognitionStage.Failed;
                _error = error;
                _stopwatch.Stop();
            }
        }

        public VideoRecognitionStatus Snapshot()
        {
            lock (_gate) { return new(_stage, _totalFrames, _completedFrames, _framesPerSecond, _stopwatch.Elapsed, _error, _lastFrameRunTimeMilliseconds); }
        }
    }
}
