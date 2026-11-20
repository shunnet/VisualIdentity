namespace Snet.Yolo.Server.anomalib;

using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using SkiaSharp;
using System.Diagnostics;
using System.Security.Cryptography;

/// <summary>复用已校验的 Anomalib 会话；空闲或出现原生执行错误时释放内存池。</summary>
public sealed class AnomalibOnnxInference : IDisposable
{
    /// <summary>连续验证时复用会话，停止操作后及时归还显存。</summary>
    private static readonly TimeSpan IdleTimeout = TimeSpan.FromSeconds(20);
    private readonly IAnomalibSessionOptionsFactory _optionsFactory;
    private readonly TimeProvider _clock;
    private readonly ITimer _idleTimer;
    private readonly SemaphoreSlim _cudaGate = new(1, 1);
    /// <summary>缓存活动会话和至多一个空闲会话，避免累积多个模型的内存池。</summary>
    private readonly Dictionary<string, AnomalibSessionEntry> _sessions = new(PathComparer);
    private readonly HashSet<AnomalibSessionEntry> _retiredSessions = [];
    private readonly object _sessionLock = new();
    private bool _disposed;

    /// <summary>使用宿主的硬件配置创建具有空闲回收的推理服务。</summary>
    public AnomalibOnnxInference(IAnomalibSessionOptionsFactory optionsFactory) : this(optionsFactory, TimeProvider.System) { }

    /// <summary>使用指定单调时钟计算空闲期限。</summary>
    internal AnomalibOnnxInference(IAnomalibSessionOptionsFactory optionsFactory, TimeProvider clock)
    {
        _optionsFactory = optionsFactory;
        _clock = clock;
        _idleTimer = clock.CreateTimer(_ => ExpireIdleSessions(), null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
    }

    /// <summary>加载图片、运行模型、提取异常区域并生成热图。</summary>
    /// <param name="onnxPath">已授权的 ONNX 模型绝对路径。</param>
    /// <param name="manifestPath">与模型摘要一致的清单路径。</param>
    /// <param name="imagePath">待识别图片的已授权本地路径。</param>
    /// <param name="cancellationToken">取消图片处理及支持终止的原生执行。</param>
    /// <param name="includeHeatmap">是否生成可预览热图；视频可关闭以减少编码开销。</param>
    /// <param name="options">区域阈值及最小异常面积；空值使用默认配置。</param>
    /// <returns>原图坐标中的结构化异常信息及可选热图。</returns>
    public async Task<AnomalibInferenceOutput> IdentifyAsync(string onnxPath, string manifestPath, string imagePath, CancellationToken cancellationToken = default, bool includeHeatmap = true, AnomalibInferenceOptions? options = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(onnxPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(manifestPath);
        options ??= new AnomalibInferenceOptions();
        options.Validate();
        cancellationToken.ThrowIfCancellationRequested();
        var snapshot = _optionsFactory.Capture();
        // 避免并发 GPU 请求同时扩张多个 PatchCore 距离矩阵的内存池。
        var usesCuda = snapshot.CacheKey.StartsWith("cuda:", StringComparison.Ordinal);
        if (usesCuda) { await _cudaGate.WaitAsync(cancellationToken).ConfigureAwait(false); }
        try
        {
            // 排队期间管理员可能已切到 CPU 或另一张 GPU，以开始执行时的配置为准。
            if (usesCuda) { snapshot = _optionsFactory.Capture(); }
            return await IdentifyCoreAsync(onnxPath, manifestPath, imagePath, options, includeHeatmap, snapshot, cancellationToken).ConfigureAwait(false);
        }
        finally { if (usesCuda) { _cudaGate.Release(); } }
    }

    /// <summary>在固定硬件上执行预处理、推理及结果解析，并确保租约始终归还。</summary>
    private async Task<AnomalibInferenceOutput> IdentifyCoreAsync(string onnxPath, string manifestPath, string imagePath,
        AnomalibInferenceOptions options, bool includeHeatmap, IAnomalibSessionOptionsFactory snapshot, CancellationToken cancellationToken)
    {
        using var lease = Acquire(onnxPath, manifestPath, snapshot);
        var session = lease.Session;
        cancellationToken.ThrowIfCancellationRequested();
        // 解码与缩放是纯 CPU 的重活（5120×5120 的 BMP 解码要几十到几百毫秒，缩放曾是单线程的 250–530 毫秒），
        // 放到线程池执行，不阻塞 Blazor 电路线程；各阶段耗时分别记录，页面日志里能看到时间花在哪。
        var stage = Stopwatch.StartNew();
        using var bitmap = await Task.Run(() => Snet.Yolo.Server.handler.ImageDecoder.Decode(imagePath), cancellationToken).ConfigureAwait(false);
        if (bitmap.Width <= 0 || bitmap.Height <= 0) { throw new InvalidDataException("图片尺寸无效。"); }
        var decodeMilliseconds = stage.ElapsedMilliseconds;
        stage.Restart();
        var tensor = await Task.Run(() => CreateInput(bitmap, session.Manifest.Input), cancellationToken).ConfigureAwait(false);
        var preprocessMilliseconds = stage.ElapsedMilliseconds;
        stage.Restart();
        using var run = new RunOptions();
        using var registration = cancellationToken.Register(() => run.Terminate = true);
        using var results = await Task.Run(() =>
        {
            var input = NamedOnnxValue.CreateFromTensor(session.Manifest.Input.Name, tensor);
            try { return session.Session.Run([input], session.Session.OutputMetadata.Keys.ToArray(), run); }
            catch (OnnxRuntimeException)
            {
                lease.Retire();
                cancellationToken.ThrowIfCancellationRequested();
                throw;
            }
        }, cancellationToken).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        var inferenceMilliseconds = stage.ElapsedMilliseconds;
        stage.Restart();
        var outputs = results.ToDictionary(item => item.Name, StringComparer.Ordinal);
        var score = ReadScalarFloat(outputs[session.Contract.PredictionScore.Name]);
        // 指定了整图判定阈值就按归一化分数判定（现场可调灵敏度，无需重训）；否则沿用模型自带的判定。
        var anomalous = options.ImageThreshold is { } imageThreshold
            ? score >= imageThreshold
            : ReadScalarBoolean(outputs[session.Contract.PredictionLabel.Name]);
        var mapTensor = outputs[session.Contract.AnomalyMap.Name].AsTensor<float>();
        var mapWidth = mapTensor.Dimensions[^1];
        var mapHeight = mapTensor.Dimensions[^2];
        if (mapWidth <= 0 || mapHeight <= 0 || (long)mapWidth * mapHeight > 16_777_216)
        {
            throw new InvalidDataException("Anomalib 异常图尺寸无效或过大。");
        }
        var map = mapTensor.ToArray();
        if (map.Length != mapWidth * mapHeight || map.Any(static value => !float.IsFinite(value)))
        {
            throw new InvalidDataException("Anomalib 异常图含有无效分数或批量维度不受支持。");
        }
        var mask = options.PixelThreshold is { } threshold
            ? AnomalibRegionProcessor.ThresholdMap(map, threshold)
            : ReadMask(outputs[session.Contract.PredictionMask.Name], map.Length);
        var regions = new List<AnomalibRegionResult>();
        foreach (var region in AnomalibRegionProcessor.Extract(mask, mapWidth, mapHeight, new AnomalibRegionOptions { MinimumArea = options.MinimumArea }))
        {
            cancellationToken.ThrowIfCancellationRequested();
            PixelRectangle original;
            try
            {
                original = AnomalibCoordinateMapper.MapToOriginal(region.Bounds, mapWidth, mapHeight,
                    session.Manifest.Input.Width, session.Manifest.Input.Height, bitmap.Width, bitmap.Height, session.Manifest.Input.ResizeMode);
            }
            catch (InvalidOperationException) { continue; }
            var (maximum, average) = ScoreRegion(map, mapWidth, region.Bounds);
            regions.Add(new AnomalibRegionResult
            {
                RegionId = regions.Count + 1,
                Bounds = original,
                PixelArea = region.PixelArea,
                MaximumScore = maximum,
                MeanScore = average,
            });
        }
        // 热图按“区域阈值”着色（未指定时用模型自带阈值），让看得见的红色范围与框保持一致。
        var heatmap = includeHeatmap
            ? AnomalibHeatmapRenderer.Render(map, mapWidth, mapHeight, options.PixelThreshold ?? session.Manifest.PostProcessing.Threshold)
            : string.Empty;
        cancellationToken.ThrowIfCancellationRequested();
        return new AnomalibInferenceOutput
        {
            Result = new AnomalibImageResult
            {
                ImageScore = score,
                IsAnomalous = anomalous,
                OriginalWidth = bitmap.Width,
                OriginalHeight = bitmap.Height,
                MapWidth = mapWidth,
                MapHeight = mapHeight,
                InferenceMilliseconds = inferenceMilliseconds,
                DecodeMilliseconds = decodeMilliseconds,
                PreprocessMilliseconds = preprocessMilliseconds,
                PostprocessMilliseconds = stage.ElapsedMilliseconds,
                Regions = regions,
            },
            HeatmapDataUrl = heatmap,
        };
    }

    /// <summary>核验模型摘要及输入输出契约后创建可复用 ONNX 会话。</summary>
    /// <param name="onnxPath">模型绝对路径。</param>
    /// <param name="manifestPath">清单路径。</param>
    /// <param name="factory">与缓存标识对应的固定硬件快照。</param>
    /// <returns>通过契约验证的原生会话。</returns>
    private AnomalibModelSession Load(string onnxPath, string manifestPath, IAnomalibSessionOptionsFactory factory)
    {
        var manifest = AnomalibManifestSerializer.Deserialize(File.ReadAllText(manifestPath));
        using (var stream = File.OpenRead(onnxPath))
        {
            var digest = Convert.ToHexStringLower(SHA256.HashData(stream));
            if (!string.Equals(digest, manifest.ModelSha256, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException("ONNX 文件与清单摘要不一致。");
            }
        }
        using var options = factory.Create();
        var session = new InferenceSession(onnxPath, options);
        try
        {
            if (!session.InputMetadata.TryGetValue(manifest.Input.Name, out var input)
                || input.ElementDataType != TensorElementType.Float
                || input.Dimensions.Length != 4
                || (manifest.Input.Layout == AnomalibTensorLayout.Nchw
                    ? input.Dimensions[1] is not (-1 or 3) || input.Dimensions[2] is not (-1 or 0) && input.Dimensions[2] != manifest.Input.Height || input.Dimensions[3] is not (-1 or 0) && input.Dimensions[3] != manifest.Input.Width
                    : input.Dimensions[3] is not (-1 or 3) || input.Dimensions[1] is not (-1 or 0) && input.Dimensions[1] != manifest.Input.Height || input.Dimensions[2] is not (-1 or 0) && input.Dimensions[2] != manifest.Input.Width))
            {
                throw new AnomalibOnnxContractException("ONNX 输入节点与 Anomalib 清单不匹配。");
            }
            var descriptors = session.OutputMetadata.Select(pair => new OnnxTensorDescriptor(pair.Key, ToElementType(pair.Value.ElementDataType), pair.Value.Dimensions.Select(static dimension => (long)dimension)));
            var contract = AnomalibOutputContractDiscovery.Discover(manifest, descriptors);
            return new AnomalibModelSession(session, manifest, contract);
        }
        catch { session.Dispose(); throw; }
    }

    /// <summary>将原图按清单描述缩放、排布和归一化为模型输入张量。</summary>
    /// <param name="source">已解码的原始图片。</param>
    /// <param name="input">清单中的输入预处理契约。</param>
    /// <returns>符合模型输入形状的 Float32 张量。</returns>
    public static DenseTensor<float> CreateInput(SKBitmap source, AnomalibInputContract input)
    {
        if (input.Interpolation == AnomalibInterpolation.BilinearAntialias)
        {
            return AnomalibAntialiasResizer.CreateInput(source, input);
        }
        using var target = new SKBitmap(new SKImageInfo(input.Width, input.Height, SKColorType.Rgba8888, SKAlphaType.Opaque));
        using (var canvas = new SKCanvas(target))
        {
            canvas.Clear(SKColors.Black);
            var scale = input.ResizeMode == AnomalibResizeMode.Letterbox
                ? Math.Min(input.Width / (float)source.Width, input.Height / (float)source.Height)
                : input.ResizeMode == AnomalibResizeMode.CenterCrop
                    ? Math.Max(input.Width / (float)source.Width, input.Height / (float)source.Height)
                    : 0f;
            var width = scale == 0 ? input.Width : source.Width * scale;
            var height = scale == 0 ? input.Height : source.Height * scale;
            var destination = SKRect.Create((input.Width - width) / 2f, (input.Height - height) / 2f, width, height);
            canvas.DrawBitmap(source, destination, new SKSamplingOptions(SKFilterMode.Linear));
        }
        var pixels = target.Pixels;
        var tensor = input.Layout == AnomalibTensorLayout.Nchw
            ? new DenseTensor<float>([1, 3, input.Height, input.Width])
            : new DenseTensor<float>([1, input.Height, input.Width, 3]);
        var divisor = input.ValueRange == AnomalibValueRange.ZeroToOne ? 255f : 1f;
        for (var y = 0; y < input.Height; y++)
        {
            for (var x = 0; x < input.Width; x++)
            {
                var pixel = pixels[y * input.Width + x];
                for (var channel = 0; channel < 3; channel++)
                {
                    var index = input.ColorSpace == AnomalibColorSpace.Bgr ? 2 - channel : channel;
                    var value = (index == 0 ? pixel.Red : index == 1 ? pixel.Green : pixel.Blue) / divisor;
                    if (!input.NormalizationEmbedded && input.Mean is not null && input.Std is not null)
                    {
                        value = (value - input.Mean[index]) / input.Std[index];
                    }
                    if (input.Layout == AnomalibTensorLayout.Nchw) { tensor[0, channel, y, x] = value; }
                    else { tensor[0, y, x, channel] = value; }
                }
            }
        }
        return tensor;
    }

    /// <summary>将 ONNX Runtime 元素类型映射到严格输出契约类型。</summary>
    /// <param name="type">原生输出元素类型。</param>
    /// <returns>清单契约中的元素类型。</returns>
    private static OnnxTensorElementType ToElementType(TensorElementType type) => type switch
    {
        TensorElementType.Float => OnnxTensorElementType.Float32,
        TensorElementType.Double => OnnxTensorElementType.Float64,
        TensorElementType.Int64 => OnnxTensorElementType.Int64,
        TensorElementType.UInt8 => OnnxTensorElementType.UInt8,
        TensorElementType.Bool => OnnxTensorElementType.Boolean,
        _ => throw new AnomalibOnnxContractException("Anomalib ONNX 输出含不支持的元素类型：" + type),
    };

    /// <summary>读取单元素的整图异常分数。</summary>
    /// <param name="value">模型返回的标量张量。</param>
    /// <returns>整图异常分数。</returns>
    private static float ReadScalarFloat(DisposableNamedOnnxValue value)
    {
        var values = value.AsTensor<float>().ToArray();
        if (values.Length != 1 || !float.IsFinite(values[0])) { throw new InvalidDataException("整图异常分数无效。"); }
        return values[0];
    }

    /// <summary>兼容布尔、整数或浮点形式的整图异常标签。</summary>
    /// <param name="value">模型返回的整图标签。</param>
    /// <returns>该图是否被模型判断为异常。</returns>
    private static bool ReadScalarBoolean(DisposableNamedOnnxValue value) => value.ElementType switch
    {
        TensorElementType.Bool => value.AsTensor<bool>().ToArray().Single(),
        TensorElementType.Int64 => value.AsTensor<long>().ToArray().Single() != 0,
        TensorElementType.Float => value.AsTensor<float>().ToArray().Single() >= 0.5f,
        _ => throw new InvalidDataException("整图异常标签的数据类型无效。"),
    };

    /// <summary>把模型二值掩码转成区域处理使用的紧凑字节数组。</summary>
    /// <param name="value">模型返回的二值掩码。</param>
    /// <param name="expectedLength">异常图所需的像素数。</param>
    /// <returns>长度经过验证的紧凑掩码。</returns>
    private static byte[] ReadMask(DisposableNamedOnnxValue value, int expectedLength)
    {
        byte[] mask = value.ElementType switch
        {
            TensorElementType.Bool => value.AsTensor<bool>().ToArray().Select(static item => item ? (byte)1 : (byte)0).ToArray(),
            TensorElementType.UInt8 => value.AsTensor<byte>().ToArray(),
            TensorElementType.Int64 => value.AsTensor<long>().ToArray().Select(static item => item == 0 ? (byte)0 : (byte)1).ToArray(),
            TensorElementType.Float => value.AsTensor<float>().ToArray().Select(static item => item >= 0.5f ? (byte)1 : (byte)0).ToArray(),
            _ => throw new InvalidDataException("异常掩码的数据类型无效。"),
        };
        if (mask.Length != expectedLength) { throw new InvalidDataException("异常掩码与异常图的尺寸不一致。"); }
        return mask;
    }

    /// <summary>统计一个异常区域外接矩形内的最大分数和平均分数。</summary>
    /// <param name="map">模型异常分数图。</param>
    /// <param name="width">异常图宽度。</param>
    /// <param name="bounds">异常图坐标中的区域范围。</param>
    /// <returns>区域最大分数和平均分数。</returns>
    private static (float Maximum, float Average) ScoreRegion(float[] map, int width, PixelRectangle bounds)
    {
        var maximum = float.NegativeInfinity;
        double sum = 0;
        for (var y = bounds.Y; y < bounds.Bottom; y++)
        {
            for (var x = bounds.X; x < bounds.Right; x++)
            {
                var value = map[y * width + x];
                maximum = Math.Max(maximum, value);
                sum += value;
            }
        }
        return (maximum, (float)(sum / (bounds.Width * bounds.Height)));
    }

    /// <summary>退役并释放指定模型的所有设备会话；活动租约结束后再释放。</summary>
    /// <param name="onnxPath">模型绝对路径。</param>
    public void Release(string onnxPath)
    {
        var (sessions, _) = RetireModel(onnxPath, waitForUsers: false);
        foreach (var session in sessions) { session.Session.Dispose(); }
    }

    /// <summary>等待该模型在当前及切换前设备上的全部活动推理结束。</summary>
    /// <param name="onnxPath">模型绝对路径。</param>
    /// <param name="cancellationToken">只取消等待，不提前释放活动会话。</param>
    public async Task ReleaseAsync(string onnxPath, CancellationToken cancellationToken = default)
    {
        var (sessions, completion) = RetireModel(onnxPath, waitForUsers: true);
        foreach (var session in sessions) { session.Session.Dispose(); }
        if (completion is not null) { await completion.WaitAsync(cancellationToken); }
    }

    /// <summary>在锁内退役匹配模型的全部条目，锁外进行释放和等待。</summary>
    /// <param name="onnxPath">模型路径。</param>
    /// <param name="waitForUsers">是否建立活动租约完成信号。</param>
    /// <returns>可立即释放的会话及可选等待任务。</returns>
    private (List<AnomalibModelSession> Sessions, Task? Completion) RetireModel(string onnxPath, bool waitForUsers)
    {
        var key = Path.GetFullPath(onnxPath);
        lock (_sessionLock)
        {
            var entries = _retiredSessions.Where(entry => PathComparer.Equals(entry.Key, key)).ToList();
            if (_sessions.Remove(key, out var current)) { entries.Add(current); }
            List<AnomalibModelSession> sessions = [];
            List<Task> waits = [];
            foreach (var entry in entries)
            {
                if (Retire(entry) is { } session) { sessions.Add(session); }
                if (entry.Users == 0) { _retiredSessions.Remove(entry); continue; }
                _retiredSessions.Add(entry);
                if (waitForUsers)
                {
                    entry.Idle ??= new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                    waits.Add(entry.Idle.Task);
                }
            }
            return (sessions, waits.Count == 0 ? null : Task.WhenAll(waits));
        }
    }

    /// <summary>硬件切换时清除缓存；活动请求完成后释放旧会话，服务仍可继续使用。</summary>
    public void Clear() => RetireAll(dispose: false);

    /// <summary>退役全部缓存；已有推理继续使用租约，结束后释放原生会话。</summary>
    public void Dispose()
    {
        RetireAll(dispose: true);
        _idleTimer.Dispose();
    }

    /// <summary>清理全部设备缓存，可选择同时禁止后续请求。</summary>
    /// <param name="dispose">是否永久关闭推理服务。</param>
    private void RetireAll(bool dispose)
    {
        List<AnomalibModelSession> sessions = [];
        lock (_sessionLock)
        {
            if (_disposed) { return; }
            _disposed = dispose;
            foreach (var entry in _sessions.Values.Concat(_retiredSessions).Distinct().ToArray())
            {
                if (Retire(entry) is { } session) { sessions.Add(session); }
                if (entry.Users != 0) { _retiredSessions.Add(entry); }
            }
            _sessions.Clear();
            _idleTimer.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
        }
        foreach (var session in sessions) { session.Session.Dispose(); }
    }

    /// <summary>按设备快照获取会话，硬件变化时替换缓存但保留旧活动租约。</summary>
    /// <param name="onnxPath">模型路径。</param>
    /// <param name="manifestPath">对应清单路径。</param>
    /// <returns>必须归还的会话租约。</returns>
    internal AnomalibSessionLease Acquire(string onnxPath, string manifestPath, IAnomalibSessionOptionsFactory? snapshot = null)
    {
        var key = Path.GetFullPath(onnxPath);
        AnomalibSessionEntry entry;
        List<AnomalibModelSession> obsolete = [];
        lock (_sessionLock)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            snapshot ??= _optionsFactory.Capture();
            foreach (var other in _sessions.Values.Where(session => session.Users == 0 && !PathComparer.Equals(session.Key, key)).ToArray())
            {
                _sessions.Remove(other.Key);
                if (Retire(other) is { } unused) { obsolete.Add(unused); }
            }
            if (!_sessions.TryGetValue(key, out entry!) || entry.HardwareKey != snapshot.CacheKey)
            {
                if (entry is not null)
                {
                    if (Retire(entry) is { } oldSession) { obsolete.Add(oldSession); }
                    if (entry.Users != 0) { _retiredSessions.Add(entry); }
                }
                entry = new AnomalibSessionEntry(key, snapshot.CacheKey,
                    new Lazy<AnomalibModelSession>(() => Load(key, manifestPath, snapshot), LazyThreadSafetyMode.ExecutionAndPublication));
                _sessions[key] = entry;
            }
            entry.Users++;
        }
        try
        {
            foreach (var session in obsolete) { session.Session.Dispose(); }
            return new AnomalibSessionLease(this, entry, entry.Value.Value);
        }
        catch { ReleaseLease(entry, failed: true); throw; }
    }

    /// <summary>从缓存退役当前租约的会话，其他已开始的使用者仍可安全结束。</summary>
    internal void RetireSession(AnomalibSessionEntry entry)
    {
        lock (_sessionLock)
        {
            if (_sessions.TryGetValue(entry.Key, out var current) && ReferenceEquals(current, entry)) { _sessions.Remove(entry.Key); }
            entry.Retired = true;
            if (entry.Users > 0) { _retiredSessions.Add(entry); }
        }
    }

    /// <summary>归还租约；只释放已退役且没有使用者的会话。</summary>
    /// <param name="entry">当前租用条目。</param>
    /// <param name="failed">会话创建是否失败；失败条目不得留在缓存。</param>
    internal void ReleaseLease(AnomalibSessionEntry entry, bool failed = false)
    {
        List<AnomalibModelSession> sessions = [];
        TaskCompletionSource<bool>? idle;
        lock (_sessionLock)
        {
            if (failed && _sessions.TryGetValue(entry.Key, out var current) && ReferenceEquals(current, entry))
            {
                _sessions.Remove(entry.Key);
                entry.Retired = true;
                if (entry.Users > 1) { _retiredSessions.Add(entry); }
            }
            entry.Users--;
            if (entry.Retired && Retire(entry) is { } retired) { sessions.Add(retired); }
            if (entry.Users == 0) { _retiredSessions.Remove(entry); }
            idle = entry.Users == 0 ? entry.Idle : null;
            if (entry.Users == 0 && !entry.Retired)
            {
                entry.IdleSince = _clock.GetTimestamp();
                foreach (var other in _sessions.Values.Where(session => session.Users == 0 && !ReferenceEquals(session, entry)).ToArray())
                {
                    _sessions.Remove(other.Key);
                    if (Retire(other) is { } unused) { sessions.Add(unused); }
                }
                _idleTimer.Change(IdleTimeout, Timeout.InfiniteTimeSpan);
            }
        }
        try { foreach (var session in sessions) { session.Session.Dispose(); } }
        finally { idle?.TrySetResult(true); }
    }

    /// <summary>回收超过空闲期限的会话；旧定时回调不能回收已被再次使用的会话。</summary>
    internal void ExpireIdleSessions()
    {
        lock (_sessionLock)
        {
            if (_disposed) { return; }
            var entry = _sessions.Values.FirstOrDefault(session => session.Users == 0);
            if (entry is null) { return; }
            var remaining = IdleTimeout - _clock.GetElapsedTime(entry.IdleSince);
            if (remaining > TimeSpan.Zero)
            {
                _idleTimer.Change(remaining, Timeout.InfiniteTimeSpan);
                return;
            }
            _sessions.Remove(entry.Key);
            // 在锁内释放已无使用者的原生会话，避免新一次 GPU 分配与旧池释放重叠。
            Retire(entry)?.Session.Dispose();
        }
    }

    /// <summary>取得无使用者条目的一次性释放权。</summary>
    /// <param name="entry">待退役的条目。</param>
    /// <returns>可释放的已加载会话；仍在使用或已经释放时为空。</returns>
    private static AnomalibModelSession? Retire(AnomalibSessionEntry entry)
    {
        entry.Retired = true;
        if (entry.Users != 0 || entry.Disposed) { return null; }
        entry.Disposed = true;
        return entry.Value.IsValueCreated ? entry.Value.Value : null;
    }

    /// <summary>按操作系统比较模型绝对路径。</summary>
    private static StringComparer PathComparer => OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
}
