using SkiaSharp;
using Snet.VisualIdentity.JointVerificationDemo.Configuration;
using Snet.Yolo.Server;
using Snet.Yolo.Server.anomalib;
using Snet.Yolo.Server.models.data;
using Snet.Yolo.Server.models.@enum;
using System.Diagnostics;

namespace Snet.VisualIdentity.JointVerificationDemo.Inference;

/// <summary>
/// 可重复调用的联合识别引擎：复用 Server 的模型会话、预处理和空间关联算法。
/// 一个实例串行处理图片；视频平台应在外部使用有界队列，不无限堆积待识别帧。
/// </summary>
public sealed class JointVerificationEngine : IAsyncDisposable
{
    /// <summary>初始化后固定的模型与参数，防止识别过程中切换配置。</summary>
    private readonly DemoOptions _options;
    /// <summary>Anomalib 会话缓存，首次识别校验摘要并加载，之后复用。</summary>
    private readonly AnomalibOnnxInference _anomalib;
    /// <summary>可复用的 YOLO 识别操作，不按帧创建模型。</summary>
    private readonly IdentityOperate? _yolo;
    /// <summary>执行提供程序单独持有，确保从未执行 YOLO 时也能释放模型。</summary>
    private readonly IDisposable? _provider;
    /// <summary>串行处理并协调释放，避免同一引擎的两帧交叉操作。</summary>
    private readonly SemaphoreSlim _gate = new(1, 1);
    /// <summary>一旦开始释放，拒绝新的识别请求。</summary>
    private int _disposed;

    /// <summary>校验配置并准备模型；加载失败立即释放已经创建的原生资源。</summary>
    /// <param name="options">已经解析绝对路径的模型配置。</param>
    public JointVerificationEngine(DemoOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        options.Validate();
        _options = options;
        var factory = new RuntimeFactory(options.GpuId);
        _anomalib = new AnomalibOnnxInference(factory);
        try
        {
            if (options.Mode != JointValidationMode.AnomalibOnly)
            {
                var provider = factory.CreateYolo(options.Yolo.OnnxPath);
                _provider = (IDisposable)provider;
                var expected = options.Yolo.Type == OnnxType.Segmentation
                    ? YoloDotNet.Enums.ModelType.Segmentation : YoloDotNet.Enums.ModelType.ObjectDetection;
                if (provider.OnnxData.ModelType != expected) { throw new InvalidDataException("YOLO 配置类型与 ONNX 模型任务类型不一致。"); }
                _yolo = new IdentityOperate(new IdentityData { Hardware = provider, IdentifyType = options.Yolo.Type });
            }
        }
        catch { _provider?.Dispose(); _anomalib.Dispose(); throw; }
    }

    /// <summary>执行页面同款级联：定位异常、按需识别已知缺陷、按原图坐标关联结果。</summary>
    /// <param name="imagePath">当前图片绝对路径；后续可持续传入不同图片，模型会话保持复用。</param>
    /// <param name="onStage">可选阶段回调，可接入平台日志；不要在回调内做耗时操作。</param>
    /// <param name="token">取消标记；不能强制中断已提交的原生 GPU 内核。</param>
    /// <returns>包含异常区域、缺陷类型、未匹配目标、热力图及模型耗时的结果。</returns>
    public async Task<JointValidationOutput> IdentifyAsync(string imagePath,
        Action<JointValidationStage>? onStage = null, CancellationToken token = default)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        await _gate.WaitAsync(token);
        try
        {
            ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
            AnomalibInferenceOutput? anomalib = null;
            if (_options.Mode != JointValidationMode.YoloOnly)
            {
                onStage?.Invoke(JointValidationStage.AnomalibStarted);
                anomalib = await _anomalib.IdentifyAsync(_options.Anomalib.OnnxPath, _options.Anomalib.ManifestPath,
                    imagePath, token, includeHeatmap: _options.IncludeHeatmap, options: _options.Anomalib.ToInferenceOptions());
                token.ThrowIfCancellationRequested();
                onStage?.Invoke(JointValidationStage.AnomalibCompleted);
            }
            if (_options.Mode == JointValidationMode.AnomalibOnly
                || (_options.Mode == JointValidationMode.Joint && anomalib is { Result.IsAnomalous: false }))
            {
                if (_options.Mode == JointValidationMode.Joint) { onStage?.Invoke(JointValidationStage.YoloSkippedNormal); }
                return new(anomalib, JointValidationMatcher.Match(anomalib!.Result.Regions, []), _options.Mode, false, 0);
            }

            var bytes = await File.ReadAllBytesAsync(imagePath, token);
            using var bitmap = SKBitmap.Decode(bytes) ?? throw new InvalidDataException("待识别文件不是有效图片。");
            onStage?.Invoke(JointValidationStage.YoloStarted);
            var watch = Stopwatch.StartNew();
            // IdentityOperate 的原生运算是同步内核，在工作线程执行，避免阻塞平台调用线程。
            var result = await Task.Run(async () => _options.Yolo.Type == OnnxType.Segmentation
                ? await _yolo!.RunAsync(new SegmentationData(bytes, _options.Yolo.Confidence, _options.Yolo.PixelConfidence, _options.Yolo.Iou), token)
                : await _yolo!.RunAsync(new ObjectDetectionData(bytes, _options.Yolo.Confidence, _options.Yolo.Iou), token), token);
            watch.Stop();
            token.ThrowIfCancellationRequested();
            if (!result.Status) { throw new InvalidOperationException("YOLO 识别失败：" + result.Message); }
            var detections = new List<JointDetection>();
            if (_options.Yolo.Type == OnnxType.ObjectDetection)
            {
                if (!result.GetDetails(out List<ObjectDetectionResultData>? boxes) || boxes is null) { throw new InvalidDataException("YOLO 检测结果类型不正确。"); }
                foreach (var box in boxes) { AddDetection(detections, box.Label.Name, box.Confidence, box.BoundingBox, bitmap.Width, bitmap.Height); }
            }
            else
            {
                if (!result.GetDetails(out List<SegmentationResultData>? segments) || segments is null) { throw new InvalidDataException("YOLO 分割结果类型不正确。"); }
                // 与联合验证页面一致：用分割实例的外接框关联异常区域，不把掩码伪装成多边形。
                foreach (var segment in segments) { AddDetection(detections, segment.Label.Name, segment.Confidence, segment.BoundingBox, bitmap.Width, bitmap.Height); }
            }
            onStage?.Invoke(JointValidationStage.YoloCompleted);
            return new(anomalib, JointValidationMatcher.Match(anomalib?.Result.Regions ?? [], detections),
                _options.Mode, true, watch.ElapsedMilliseconds);
        }
        finally { _gate.Release(); }
    }

    /// <summary>把 YOLO 外接框限制在原图范围内，坐标规则与 Server 联合验证服务保持一致。</summary>
    /// <param name="detections">接收有效目标的列表。</param>
    /// <param name="name">缺陷类别名称。</param>
    /// <param name="confidence">目标置信度。</param>
    /// <param name="bounds">YOLO 返回的原图像素框。</param>
    /// <param name="width">原图宽度。</param>
    /// <param name="height">原图高度。</param>
    private static void AddDetection(List<JointDetection> detections, string name, double confidence, SKRectI bounds, int width, int height)
    {
        var left = Math.Clamp(bounds.Left, 0, width);
        var top = Math.Clamp(bounds.Top, 0, height);
        var right = Math.Clamp(bounds.Right, 0, width);
        var bottom = Math.Clamp(bounds.Bottom, 0, height);
        if (right > left && bottom > top) { detections.Add(new(name, confidence, new(left, top, right - left, bottom - top))); }
    }

    /// <summary>停止接收请求，等待当前识别完成后释放两类模型；退出前调用并等待。</summary>
    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) { return; }
        await _gate.WaitAsync();
        try
        {
            try { if (_yolo is not null) { await _yolo.DisposeAsync(); } }
            finally
            {
                try { _provider?.Dispose(); }
                finally { _anomalib.Dispose(); }
            }
        }
        finally { _gate.Release(); }
        // 不释放门闩：已经排队的调用仍需醒来、检查释放状态并安全退出；门闩没有创建原生等待句柄。
    }
}
