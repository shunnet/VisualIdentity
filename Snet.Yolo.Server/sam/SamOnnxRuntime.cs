using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using SkiaSharp;

namespace Snet.Yolo.Server.sam;

/// <summary>共享 ONNX 会话；最多驻留一套模型，页面持有各自图片编码。</summary>
public sealed class SamOnnxRuntime : IDisposable
{
    private readonly SamModelStore models;
    private readonly Func<int?, SessionOptions> optionsFactory;
    /// <summary>宿主是否支持指定 CUDA GPU；实际环境在创建会话时验证。</summary>
    public bool SupportsCuda { get; }
    /// <summary>创建 CPU 运行时，保留原有会话工厂调用方式。</summary>
    public SamOnnxRuntime(SamModelStore models, Func<SessionOptions> optionsFactory)
        : this(models, _ => optionsFactory(), false) { }
    /// <summary>创建可按请求选择 CPU 或单张 CUDA GPU 的运行时。</summary>
    public SamOnnxRuntime(SamModelStore models, Func<int?, SessionOptions> optionsFactory, bool supportsCuda)
    { this.models = models; this.optionsFactory = optionsFactory; SupportsCuda = supportsCuda; }
    private readonly SemaphoreSlim _gate = new(1, 1);
    private InferenceSession? _encoder, _decoder;
    private bool _disposed;
    private SamInstalledModel? _loadedModel;
    private int? _loadedGpu;

    /// <summary>准备共享权重；下载进度为 0–100。</summary>
    public Task PrepareAsync(Action<int>? progress = null, CancellationToken cancellationToken = default)
        => models.EnsureReadyAsync(progress, cancellationToken);

    /// <summary>只准备用户选择的模型。</summary>
    public Task PrepareAsync(SamModelKind kind, Action<int>? progress = null, CancellationToken cancellationToken = default)
        => models.EnsureReadyAsync(kind, progress, cancellationToken);

    /// <summary>读取已安装版本，不自动更新。</summary>
    public SamUpdateStatus GetVersionStatus(SamModelKind kind) => models.GetVersionStatus(kind);

    /// <summary>检查最新兼容权重；未知权重只提示，不下载安装。</summary>
    public Task<SamUpdateStatus> CheckForUpdatesAsync(SamModelKind kind, CancellationToken cancellationToken = default)
        => models.CheckForUpdatesAsync(kind, cancellationToken);

    /// <summary>按所选设备验证新会话；通过后才切换版本，失败保留原文件与版本记录。</summary>
    public Task UpdateAsync(SamModelKind kind, int? gpuId, Action<int>? progress = null, CancellationToken cancellationToken = default)
    {
        ValidateDevice(gpuId);
        return models.UpdateAsync(kind, (installed, token) => ValidateVersionAsync(installed, gpuId, token), progress, cancellationToken);
    }

    /// <summary>验证并切换到上一版本；已编码图片仍使用各自的版本。</summary>
    public Task RollbackAsync(SamModelKind kind, int? gpuId, CancellationToken cancellationToken = default)
    {
        ValidateDevice(gpuId);
        return models.RollbackAsync(kind, (installed, token) => ValidateVersionAsync(installed, gpuId, token), cancellationToken);
    }

    private async Task ValidateVersionAsync(SamInstalledModel installed, int? gpuId, CancellationToken token)
    {
        await _gate.WaitAsync(token);
        try
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            await Task.Run(() => { EnsureSessions(installed, gpuId); ValidateExecution(installed.Definition.Kind, token); }, token);
            token.ThrowIfCancellationRequested();
        }
        catch (OnnxRuntimeException) when (token.IsCancellationRequested) { throw new OperationCanceledException(token); }
        finally { _gate.Release(); }
    }

    /// <summary>从调用者已授权的本地图片提取编码；最长边缩放到 1024，最多支持 2400 万原图像素。</summary>
    public async Task<SamImageContext> EncodeAsync(string imagePath, CancellationToken cancellationToken = default)
        => await EncodeAsync(imagePath, SamModelKind.MobileSam, cancellationToken);

    /// <summary>按所选模型预处理图片并缓存全部特征。</summary>
    public async Task<SamImageContext> EncodeAsync(string imagePath, SamModelKind kind, CancellationToken cancellationToken = default)
        => await EncodeAsync(imagePath, kind, null, cancellationToken);

    /// <summary>在 CPU（null）或指定 GPU 上编码；一张图片不跨多 GPU 拆分。</summary>
    public async Task<SamImageContext> EncodeAsync(string imagePath, SamModelKind kind, int? gpuId, CancellationToken cancellationToken = default)
    {
        ValidateDevice(gpuId);
        await PrepareAsync(kind, cancellationToken: cancellationToken);
        var installed = models.GetInstalled(kind);
        await _gate.WaitAsync(cancellationToken);
        try
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            return await Task.Run(() =>
            {
                EnsureSessions(installed, gpuId);
                using var codec = SKCodec.Create(imagePath) ?? throw new InvalidDataException("SAM 无法读取图片。");
                var w = codec.Info.Width; var h = codec.Info.Height;
                if (w <= 0 || h <= 0 || (long)w * h > 24_000_000) { throw new InvalidDataException("SAM 图片不能超过 2400 万像素。"); }
                using var original = SKBitmap.Decode(codec) ?? throw new InvalidDataException("SAM 图片解码失败。");
                // 此固定编码器只做归一化与 padding，不含 resize；小图也必须放大到最长边 1024。
                var scale = 1024d / Math.Max(w, h);
                var isSam2 = kind == SamModelKind.Sam21Tiny;
                var ww = isSam2 ? 1024 : Math.Max(1, (int)Math.Round(w * scale)); var hh = isSam2 ? 1024 : Math.Max(1, (int)Math.Round(h * scale));
                using var resized = new SKBitmap(ww, hh);
                using (var canvas = new SKCanvas(resized))
                { canvas.DrawBitmap(original, new SKRect(0, 0, ww, hh), new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.None)); }
                var input = new float[checked(ww * hh * 3)];
                for (var y = 0; y < hh; y++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    for (var x = 0; x < ww; x++)
                    {
                        var c = resized.GetPixel(x, y); var pixel = y * ww + x;
                        if (isSam2) { input[pixel] = (c.Red / 255f - .485f) / .229f; input[ww * hh + pixel] = (c.Green / 255f - .456f) / .224f; input[2 * ww * hh + pixel] = (c.Blue / 255f - .406f) / .225f; }
                        else { var i = pixel * 3; input[i] = c.Red; input[i + 1] = c.Green; input[i + 2] = c.Blue; }
                    }
                }
                using var run = new RunOptions();
                using var registration = cancellationToken.Register(() => run.Terminate = true);
                var outputNames = isSam2 ? new[] { "image_embed", "high_res_feats_0", "high_res_feats_1" } : ["image_embeddings"];
                using var output = _encoder!.Run([NamedOnnxValue.CreateFromTensor(isSam2 ? "image" : "input_image", new DenseTensor<float>(input, isSam2 ? [1, 3, 1024, 1024] : [hh, ww, 3]))], outputNames, run);
                cancellationToken.ThrowIfCancellationRequested();
                return new SamImageContext { Installed = installed, ModelKind = kind, GpuId = gpuId, Width = w, Height = h, WorkWidth = ww, WorkHeight = hh,
                    Embedding = output.First(o => o.Name == outputNames[0]).AsTensor<float>().ToArray(),
                    HighRes0 = isSam2 ? output.First(o => o.Name == "high_res_feats_0").AsTensor<float>().ToArray() : [],
                    HighRes1 = isSam2 ? output.First(o => o.Name == "high_res_feats_1").AsTensor<float>().ToArray() : [] };
            }, cancellationToken);
        }
        catch (OnnxRuntimeException) when (cancellationToken.IsCancellationRequested) { throw new OperationCanceledException(cancellationToken); }
        finally { _gate.Release(); }
    }

    /// <summary>以原图像素坐标的前景/背景点生成掩码；点列最多 64 个，首点必须是前景。</summary>
    public async Task<SamResult> SegmentAsync(SamImageContext image, IReadOnlyList<SamPrompt> prompts, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(image); ArgumentNullException.ThrowIfNull(prompts);
        ValidateDevice(image.GpuId);
        if (prompts.Count is < 1 or > 64 || !prompts[0].Include || prompts.Any(p => !double.IsFinite(p.X) || !double.IsFinite(p.Y) || p.X < 0 || p.Y < 0 || p.X >= image.Width || p.Y >= image.Height))
        { throw new ArgumentException("SAM 需要 1–64 个图片内的点，首点必须是前景点。", nameof(prompts)); }
        var snapshot = prompts.ToArray();
        if (image.Installed is null) { await PrepareAsync(image.ModelKind, cancellationToken: cancellationToken); }
        var installed = image.Installed ?? models.GetInstalled(image.ModelKind);
        await _gate.WaitAsync(cancellationToken);
        try
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            return await Task.Run(() =>
            {
                EnsureSessions(installed, image.GpuId);
                var isSam2 = image.ModelKind == SamModelKind.Sam21Tiny;
                var n = snapshot.Length + (isSam2 ? 0 : 1); var coords = new float[n * 2]; var labels = new float[n];
                if (!isSam2) { labels[^1] = -1; }
                var resizeScale = 1024d / Math.Max(image.WorkWidth, image.WorkHeight);
                // 与编码器的 longest-side resize 一致，短边取整后分别换算坐标。
                var rw = (int)(image.WorkWidth * resizeScale + 0.5); var rh = (int)(image.WorkHeight * resizeScale + 0.5);
                for (var i = 0; i < snapshot.Length; i++)
                { coords[i * 2] = (float)(snapshot[i].X * rw / image.Width); coords[i * 2 + 1] = (float)(snapshot[i].Y * rh / image.Height); labels[i] = snapshot[i].Include ? 1 : 0; }
                var inputs = new List<NamedOnnxValue> {
                    NamedOnnxValue.CreateFromTensor(isSam2 ? "image_embed" : "image_embeddings", new DenseTensor<float>(image.Embedding, [1, 256, 64, 64])),
                    NamedOnnxValue.CreateFromTensor("point_coords", new DenseTensor<float>(coords, [1, n, 2])),
                    NamedOnnxValue.CreateFromTensor("point_labels", new DenseTensor<float>(labels, [1, n])),
                    NamedOnnxValue.CreateFromTensor("mask_input", new DenseTensor<float>(new float[256 * 256], [1, 1, 256, 256])),
                    NamedOnnxValue.CreateFromTensor("has_mask_input", new DenseTensor<float>(new float[1], [1])) };
                if (isSam2)
                {
                    inputs.Add(NamedOnnxValue.CreateFromTensor("high_res_feats_0", new DenseTensor<float>(image.HighRes0, [1, 32, 256, 256])));
                    inputs.Add(NamedOnnxValue.CreateFromTensor("high_res_feats_1", new DenseTensor<float>(image.HighRes1, [1, 64, 128, 128])));
                }
                else { inputs.Add(NamedOnnxValue.CreateFromTensor("orig_im_size", new DenseTensor<float>(new float[] { image.WorkHeight, image.WorkWidth }, [2]))); }
                using var run = new RunOptions(); using var registration = cancellationToken.Register(() => run.Terminate = true);
                using var outputs = _decoder!.Run(inputs, ["masks", "iou_predictions"], run);
                cancellationToken.ThrowIfCancellationRequested();
                var masks = outputs.First(o => o.Name == "masks").AsTensor<float>();
                var scores = outputs.First(o => o.Name == "iou_predictions").AsTensor<float>().ToArray();
                var wh = image.WorkWidth * image.WorkHeight;
                var mw = masks.Dimensions[3]; var mh = masks.Dimensions[2];
                if (masks.Length != mw * mh * scores.Length) { throw new InvalidDataException("SAM 解码掩码尺寸不匹配。"); }
                var data = ResizeMasks(masks.ToArray(), mw, mh, image.WorkWidth, image.WorkHeight, scores.Length, cancellationToken);
                var best = -1; var quality = float.NegativeInfinity;
                for (var m = 0; m < scores.Length; m++)
                {
                    var matches = snapshot.All(p => (data[m * wh + Math.Min(image.WorkHeight - 1, (int)(p.Y * image.WorkHeight / image.Height)) * image.WorkWidth + Math.Min(image.WorkWidth - 1, (int)(p.X * image.WorkWidth / image.Width))] > 0) == p.Include);
                    if (matches && float.IsFinite(scores[m]) && scores[m] > quality) { best = m; quality = scores[m]; }
                }
                if (best < 0) { throw new InvalidOperationException("SAM 未找到满足提示点的物体，请取消后重新点选或调整排除点。"); }
                var binary = new byte[wh];
                for (var i = 0; i < wh; i++) { binary[i] = data[best * wh + i] > 0 ? (byte)255 : (byte)0; }
                var result = SamMaskGeometry.Create(binary, image.WorkWidth, image.WorkHeight, image.Width, image.Height,
                    (int)(snapshot[0].X * image.WorkWidth / image.Width), (int)(snapshot[0].Y * image.WorkHeight / image.Height), quality, cancellationToken);
                if (snapshot.Any(p => (result.Mask[(int)p.Y * image.Width + (int)p.X] != 0) != p.Include))
                { throw new InvalidOperationException("SAM 提示点不属于同一个连通物体，请取消后分开标注。"); }
                return result;
            }, cancellationToken);
        }
        catch (OnnxRuntimeException) when (cancellationToken.IsCancellationRequested) { throw new OperationCanceledException(cancellationToken); }
        finally { _gate.Release(); }
    }

    private void ValidateDevice(int? gpuId)
    {
        if (gpuId < 0) { throw new ArgumentOutOfRangeException(nameof(gpuId)); }
        if (gpuId is not null && !SupportsCuda) { throw new NotSupportedException("当前发行包不支持 SAM GPU 推理，请使用 CUDA 发行包。"); }
    }

    private void EnsureSessions(SamInstalledModel installed, int? gpuId)
    {
        if (_loadedModel == installed && _loadedGpu == gpuId && _encoder is not null && _decoder is not null) { return; }
        var kind = installed.Definition.Kind;
        // 切换前释放旧会话，避免两套权重同时占用内存/显存；其他用户的缓存带模型标识。
        _decoder?.Dispose(); _encoder?.Dispose(); _decoder = null; _encoder = null; _loadedModel = null;
        using var encoderOptions = optionsFactory(gpuId); using var decoderOptions = optionsFactory(gpuId);
        InferenceSession? encoder = null, decoder = null;
        try
        {
            encoder = new(Path.Combine(installed.Directory, installed.Definition.Encoder), encoderOptions);
            decoder = new(Path.Combine(installed.Directory, installed.Definition.Decoder), decoderOptions);
            ValidateContract(encoder, decoder, kind);
            _encoder = encoder; _decoder = decoder; _loadedModel = installed; _loadedGpu = gpuId;
        }
        catch { encoder?.Dispose(); decoder?.Dispose(); throw; }
    }

    private static void ValidateContract(InferenceSession encoder, InferenceSession decoder, SamModelKind kind)
    {
        var sam2 = kind == SamModelKind.Sam21Tiny;
        static bool Has(IReadOnlyDictionary<string, NodeMetadata> metadata, string name, int rank)
            => metadata.TryGetValue(name, out var node) && node.IsTensor && node.ElementType == typeof(float) && node.Dimensions.Length == rank;
        var embedding = sam2 ? "image_embed" : "image_embeddings";
        if (!Has(encoder.InputMetadata, sam2 ? "image" : "input_image", sam2 ? 4 : 3) ||
            !Has(encoder.OutputMetadata, embedding, 4) || !Has(decoder.InputMetadata, embedding, 4) ||
            !Has(decoder.InputMetadata, "point_coords", 3) || !Has(decoder.InputMetadata, "point_labels", 2) ||
            !Has(decoder.InputMetadata, "mask_input", 4) || !Has(decoder.InputMetadata, "has_mask_input", 1) ||
            !Has(decoder.OutputMetadata, "masks", 4) || !Has(decoder.OutputMetadata, "iou_predictions", 2) ||
            (!sam2 && !Has(decoder.InputMetadata, "orig_im_size", 1)) ||
            (sam2 && (!Has(encoder.OutputMetadata, "high_res_feats_0", 4) || !Has(encoder.OutputMetadata, "high_res_feats_1", 4) ||
                !Has(decoder.InputMetadata, "high_res_feats_0", 4) || !Has(decoder.InputMetadata, "high_res_feats_1", 4))))
        { throw new InvalidDataException("SAM ONNX 模型接口不匹配，版本未切换。"); }
    }

    /// <summary>提交前在目标设备执行编码与解码，发现算子/驱动错误；不要求合成图产生语义物体。</summary>
    private void ValidateExecution(SamModelKind kind, CancellationToken token)
    {
        var sam2 = kind == SamModelKind.Sam21Tiny; var embedding = sam2 ? "image_embed" : "image_embeddings";
        using var run = new RunOptions(); using var registration = token.Register(() => run.Terminate = true);
        var outputNames = sam2 ? new[] { embedding, "high_res_feats_0", "high_res_feats_1" } : [embedding];
        using var features = _encoder!.Run([NamedOnnxValue.CreateFromTensor(sam2 ? "image" : "input_image",
            new DenseTensor<float>(new float[1024 * 1024 * 3], sam2 ? [1, 3, 1024, 1024] : [1024, 1024, 3]))], outputNames, run);
        token.ThrowIfCancellationRequested();
        var inputs = new List<NamedOnnxValue> {
            NamedOnnxValue.CreateFromTensor(embedding, features.First(f => f.Name == embedding).AsTensor<float>()),
            NamedOnnxValue.CreateFromTensor("point_coords", new DenseTensor<float>(sam2 ? new float[] { 512, 512 } : new float[] { 512, 512, 0, 0 }, sam2 ? [1, 1, 2] : [1, 2, 2])),
            NamedOnnxValue.CreateFromTensor("point_labels", new DenseTensor<float>(sam2 ? new float[] { 1 } : new float[] { 1, -1 }, sam2 ? [1, 1] : [1, 2])),
            NamedOnnxValue.CreateFromTensor("mask_input", new DenseTensor<float>(new float[256 * 256], [1, 1, 256, 256])),
            NamedOnnxValue.CreateFromTensor("has_mask_input", new DenseTensor<float>(new float[1], [1])) };
        if (sam2)
        {
            inputs.Add(NamedOnnxValue.CreateFromTensor("high_res_feats_0", features.First(f => f.Name == "high_res_feats_0").AsTensor<float>()));
            inputs.Add(NamedOnnxValue.CreateFromTensor("high_res_feats_1", features.First(f => f.Name == "high_res_feats_1").AsTensor<float>()));
        }
        else { inputs.Add(NamedOnnxValue.CreateFromTensor("orig_im_size", new DenseTensor<float>(new float[] { 1024, 1024 }, [2]))); }
        using var masks = _decoder!.Run(inputs, ["masks", "iou_predictions"], run);
        token.ThrowIfCancellationRequested();
        if (masks.Any(m => m.AsTensor<float>().Length == 0 || m.AsTensor<float>().Any(v => !float.IsFinite(v))))
        { throw new InvalidDataException("SAM 版本试运行输出无效，版本未切换。"); }
    }

    private static float[] ResizeMasks(float[] source, int width, int height, int targetWidth, int targetHeight, int count, CancellationToken token)
    {
        if (width == targetWidth && height == targetHeight) { return source; }
        var output = new float[checked(targetWidth * targetHeight * count)];
        for (var m = 0; m < count; m++)
        for (var y = 0; y < targetHeight; y++)
        {
            token.ThrowIfCancellationRequested();
            var fy = Math.Clamp((y + .5) * height / targetHeight - .5, 0, height - 1); var y0 = (int)fy; var y1 = Math.Min(y0 + 1, height - 1); var dy = (float)(fy - y0);
            for (var x = 0; x < targetWidth; x++)
            {
                var fx = Math.Clamp((x + .5) * width / targetWidth - .5, 0, width - 1); var x0 = (int)fx; var x1 = Math.Min(x0 + 1, width - 1); var dx = (float)(fx - x0); var offset = m * width * height;
                var a = source[offset + y0 * width + x0] * (1 - dx) + source[offset + y0 * width + x1] * dx;
                var b = source[offset + y1 * width + x0] * (1 - dx) + source[offset + y1 * width + x1] * dx;
                output[(m * targetHeight + y) * targetWidth + x] = a * (1 - dy) + b * dy;
            }
        }
        return output;
    }

    /// <summary>应用关闭时释放原生会话；与正在运行的推理互斥。</summary>
    public void Dispose()
    {
        _gate.Wait();
        try { if (_disposed) { return; } _disposed = true; _decoder?.Dispose(); _encoder?.Dispose(); }
        finally { _gate.Release(); }
    }
}
