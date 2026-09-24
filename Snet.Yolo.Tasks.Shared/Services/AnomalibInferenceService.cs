namespace Snet.Yolo.Tasks.Services;

using System.Collections.Concurrent;
using System.Diagnostics;
using System.Security.Cryptography;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using SkiaSharp;
using Snet.Yolo.Tasks.Core.Anomalib;

/// <summary>为当前部署构建创建 ONNX Runtime 会话选项。</summary>
public interface IAnomalibSessionOptionsFactory
{
    /// <summary>创建带有当前构建可用执行提供程序的会话选项。</summary>
    SessionOptions Create();
}

/// <summary>一次异常检测的结构化结果和可直接预览的低分辨率热图。</summary>
public sealed class AnomalibInferenceOutput
{
    /// <summary>图像级分数和原图坐标区域。</summary>
    public required AnomalibImageResult Result { get; init; }

    /// <summary>透明 PNG 热图的 data URL。</summary>
    public required string HeatmapDataUrl { get; init; }
}

/// <summary>在独立 ONNX Runtime 会话中执行 Anomalib 推理；同一页面电路复用已校验的模型。</summary>
public sealed class AnomalibInferenceService(IAnomalibSessionOptionsFactory optionsFactory) : IDisposable
{
    /// <summary>按 ONNX 绝对路径缓存会话，避免每张图片重新加载模型。</summary>
    private readonly ConcurrentDictionary<string, Lazy<ModelSession>> _sessions = new(PathComparer);

    /// <summary>加载图片、运行模型、提取异常区域并生成热图。</summary>
    public async Task<AnomalibInferenceOutput> IdentifyAsync(RegisteredAnomalibModel model, string imagePath, CancellationToken cancellationToken = default, bool includeHeatmap = true)
    {
        ArgumentNullException.ThrowIfNull(model);
        var session = _sessions.GetOrAdd(Path.GetFullPath(model.OnnxPath), _ => new Lazy<ModelSession>(() => Load(model), LazyThreadSafetyMode.ExecutionAndPublication)).Value;
        cancellationToken.ThrowIfCancellationRequested();
        using var bitmap = SKBitmap.Decode(imagePath) ?? throw new InvalidDataException("无法解码待识别图片。");
        if (bitmap.Width <= 0 || bitmap.Height <= 0) { throw new InvalidDataException("图片尺寸无效。"); }
        var tensor = CreateInput(bitmap, session.Manifest.Input);
        var timer = Stopwatch.StartNew();
        using var results = await Task.Run(() =>
        {
            var input = NamedOnnxValue.CreateFromTensor(session.Manifest.Input.Name, tensor);
            return session.Session.Run([input]);
        }, cancellationToken);
        timer.Stop();
        var outputs = results.ToDictionary(item => item.Name, StringComparer.Ordinal);
        var score = ReadScalarFloat(outputs[session.Contract.PredictionScore.Name]);
        var anomalous = ReadScalarBoolean(outputs[session.Contract.PredictionLabel.Name]);
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
        var mask = ReadMask(outputs[session.Contract.PredictionMask.Name], map.Length);
        var regions = new List<AnomalibRegionResult>();
        foreach (var region in AnomalibRegionProcessor.Extract(mask, mapWidth, mapHeight, new AnomalibRegionOptions { MinimumArea = 4 }))
        {
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
                InferenceMilliseconds = timer.ElapsedMilliseconds,
                Regions = regions,
            },
            HeatmapDataUrl = includeHeatmap ? CreateHeatmap(map, mapWidth, mapHeight) : string.Empty,
        };
    }

    /// <summary>核验模型摘要及输入输出契约后创建可复用 ONNX 会话。</summary>
    private ModelSession Load(RegisteredAnomalibModel model)
    {
        var manifest = AnomalibManifestSerializer.Deserialize(File.ReadAllText(model.ManifestPath));
        using (var stream = File.OpenRead(model.OnnxPath))
        {
            var digest = Convert.ToHexStringLower(SHA256.HashData(stream));
            if (!string.Equals(digest, manifest.ModelSha256, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException("ONNX 文件与清单摘要不一致。");
            }
        }
        using var options = optionsFactory.Create();
        var session = new InferenceSession(model.OnnxPath, options);
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
            return new ModelSession(session, manifest, contract);
        }
        catch { session.Dispose(); throw; }
    }

    /// <summary>将原图按清单描述缩放、排布和归一化为模型输入张量。</summary>
    internal static DenseTensor<float> CreateInput(SKBitmap source, AnomalibInputContract input)
    {
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
    private static float ReadScalarFloat(DisposableNamedOnnxValue value)
    {
        var values = value.AsTensor<float>().ToArray();
        if (values.Length != 1 || !float.IsFinite(values[0])) { throw new InvalidDataException("整图异常分数无效。"); }
        return values[0];
    }

    /// <summary>兼容布尔、整数或浮点形式的整图异常标签。</summary>
    private static bool ReadScalarBoolean(DisposableNamedOnnxValue value) => value.ElementType switch
    {
        TensorElementType.Bool => value.AsTensor<bool>().ToArray().Single(),
        TensorElementType.Int64 => value.AsTensor<long>().ToArray().Single() != 0,
        TensorElementType.Float => value.AsTensor<float>().ToArray().Single() >= 0.5f,
        _ => throw new InvalidDataException("整图异常标签的数据类型无效。"),
    };

    /// <summary>把模型二值掩码转成区域处理使用的紧凑字节数组。</summary>
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

    /// <summary>把异常图渲染成低分辨率透明红色热图，供验证页单独查看。</summary>
    private static string CreateHeatmap(float[] map, int width, int height)
    {
        var minimum = map.Min();
        var maximum = map.Max();
        var span = maximum - minimum;
        using var bitmap = new SKBitmap(new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Premul));
        var pixels = new SKColor[map.Length];
        for (var index = 0; index < map.Length; index++)
        {
            var intensity = span <= 1e-6f ? 0f : Math.Clamp((map[index] - minimum) / span, 0f, 1f);
            pixels[index] = new SKColor(255, (byte)(128 * (1 - intensity)), 0, (byte)(220 * intensity));
        }
        bitmap.Pixels = pixels;
        using var image = SKImage.FromBitmap(bitmap);
        using var encoded = image.Encode(SKEncodedImageFormat.Png, 90);
        return "data:image/png;base64," + Convert.ToBase64String(encoded.ToArray());
    }

    /// <summary>释放当前电路复用的全部 ONNX Runtime 会话。</summary>
    /// <summary>移除并释放指定模型的缓存会话，便于删除其磁盘产物。</summary>
    public void Release(string onnxPath)
    {
        if (_sessions.TryRemove(Path.GetFullPath(onnxPath), out var entry) && entry.IsValueCreated)
        {
            entry.Value.Session.Dispose();
        }
    }

    public void Dispose()
    {
        foreach (var entry in _sessions.Values) { if (entry.IsValueCreated) { entry.Value.Session.Dispose(); } }
        _sessions.Clear();
    }

    /// <summary>根据操作系统选择路径字典的比较规则。</summary>
    private static StringComparer PathComparer => OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;

    /// <summary>已校验并可复用的 ONNX 会话及其语义契约。</summary>
    private sealed record ModelSession(InferenceSession Session, AnomalibModelManifest Manifest, DiscoveredAnomalibOutputContract Contract);
}
