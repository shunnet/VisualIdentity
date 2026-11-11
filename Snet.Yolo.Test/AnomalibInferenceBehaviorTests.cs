using Microsoft.ML.OnnxRuntime;
using SkiaSharp;
using Snet.Yolo.Server.anomalib;
using System.Security.Cryptography;
using Xunit;

namespace Snet.Yolo.Test;

/// <summary>
/// 推理流程的行为：整图判定阈值可调、各阶段耗时分别记录、热图是体积受限的 PNG。
/// 使用固定输出的 ONNX 夹具（整图分数 0.75、模型判定为异常、4×4 异常图 0→1 线性递增）。
/// </summary>
public sealed class AnomalibInferenceBehaviorTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "snet-anomalib-behavior-" + Guid.NewGuid().ToString("N"));
    private readonly AnomalibOnnxInference _inference = new(new CpuOptionsFactory());
    private readonly string _onnxPath;
    private readonly string _manifestPath;
    private readonly string _imagePath;

    public AnomalibInferenceBehaviorTests()
    {
        Directory.CreateDirectory(_root);
        var bytes = ApiModelFixtures.AnomalibOnnx;
        _onnxPath = Path.Combine(_root, "model.onnx");
        _manifestPath = Path.Combine(_root, "model.manifest.json");
        _imagePath = Path.Combine(_root, "white.png");
        File.WriteAllBytes(_onnxPath, bytes);
        File.WriteAllText(_manifestPath, AnomalibManifestSerializer.Serialize(new AnomalibModelManifest
        {
            SchemaVersion = "1.0",
            Family = "anomalib",
            AnomalibVersion = "test",
            Algorithm = AnomalibAlgorithm.PatchCore,
            ModelSha256 = Convert.ToHexStringLower(SHA256.HashData(bytes)),
            Input = new AnomalibInputContract
            {
                Name = "image", ElementType = OnnxTensorElementType.Float32, Layout = AnomalibTensorLayout.Nchw,
                ColorSpace = AnomalibColorSpace.Rgb, Width = 32, Height = 32, ResizeMode = AnomalibResizeMode.Stretch,
                ValueRange = AnomalibValueRange.ZeroToOne, NormalizationEmbedded = true,
            },
            Outputs = new AnomalibOutputNames
            {
                PredictionScore = "pred_score", PredictionLabel = "pred_label", AnomalyMap = "anomaly_map", PredictionMask = "pred_mask",
            },
            PostProcessing = new AnomalibPostProcessingContract { Threshold = 0.5f, ThresholdSource = AnomalibThresholdSource.Manual },
        }));
        using var bitmap = new SKBitmap(32, 32);
        bitmap.Erase(SKColors.White);
        using var image = SKImage.FromBitmap(bitmap);
        using var encoded = image.Encode(SKEncodedImageFormat.Png, 90);
        File.WriteAllBytes(_imagePath, encoded.ToArray());
    }

    public void Dispose()
    {
        _inference.Release(_onnxPath);
        _inference.Dispose();
        if (Directory.Exists(_root)) { Directory.Delete(_root, recursive: true); }
    }

    private Task<AnomalibInferenceOutput> IdentifyAsync(AnomalibInferenceOptions? options = null)
        => _inference.IdentifyAsync(_onnxPath, _manifestPath, _imagePath, TestContext.Current.CancellationToken, true, options);

    /// <summary>不指定整图阈值时沿用模型自带的判定；指定后按归一化分数判定，现场可以不重训就调灵敏度。</summary>
    [Theory]
    [InlineData(null, true)]
    [InlineData(0.5f, true)]
    [InlineData(0.75f, true)]
    [InlineData(0.76f, false)]
    [InlineData(0.9f, false)]
    [InlineData(0.0f, true)]
    public async Task ImageThreshold_DecidesTheVerdictFromTheNormalizedScore(float? threshold, bool expectedAnomalous)
    {
        var output = await IdentifyAsync(new AnomalibInferenceOptions { ImageThreshold = threshold });

        Assert.Equal(0.75f, output.Result.ImageScore);
        Assert.Equal(expectedAnomalous, output.Result.IsAnomalous);
    }

    [Theory]
    [InlineData(-0.1f)]
    [InlineData(1.1f)]
    [InlineData(float.NaN)]
    public void ImageThreshold_OutsideZeroToOne_IsRejected(float threshold)
        => Assert.Throws<ArgumentOutOfRangeException>(() => new AnomalibInferenceOptions { ImageThreshold = threshold }.Validate());

    /// <summary>解码、预处理、推理、后处理分别计时，总耗时是它们之和，日志里能直接看出时间花在哪。</summary>
    [Fact]
    public async Task Result_ReportsEveryStageAndTheirSum()
    {
        var result = (await IdentifyAsync()).Result;

        Assert.True(result.DecodeMilliseconds >= 0);
        Assert.True(result.PreprocessMilliseconds >= 0);
        Assert.True(result.InferenceMilliseconds >= 0);
        Assert.True(result.PostprocessMilliseconds >= 0);
        Assert.Equal(result.DecodeMilliseconds + result.PreprocessMilliseconds + result.InferenceMilliseconds + result.PostprocessMilliseconds,
            result.TotalMilliseconds);
    }

    [Fact]
    public async Task Heatmap_IsATransparentPng_WithinTheSizeCap()
    {
        var output = await IdentifyAsync();

        const string prefix = "data:image/png;base64,";
        Assert.StartsWith(prefix, output.HeatmapDataUrl);
        using var codec = SKCodec.Create(new MemoryStream(Convert.FromBase64String(output.HeatmapDataUrl[prefix.Length..])))!;
        Assert.True(Math.Max(codec.Info.Width, codec.Info.Height) <= AnomalibHeatmapRenderer.MaximumEdge);
        Assert.Equal((4, 4), (codec.Info.Width, codec.Info.Height));      // 夹具的异常图只有 4×4，不放大
    }

    /// <summary>区域阈值仍然只影响框，不改变整图分数；阈值越高区域越少。</summary>
    [Fact]
    public async Task PixelThreshold_ControlsRegionsOnly()
    {
        var loose = await IdentifyAsync(new AnomalibInferenceOptions { PixelThreshold = 0.3f, MinimumArea = 1 });
        var strict = await IdentifyAsync(new AnomalibInferenceOptions { PixelThreshold = 0.95f, MinimumArea = 1 });

        Assert.Equal(loose.Result.ImageScore, strict.Result.ImageScore);
        Assert.True(loose.Result.Regions.Sum(region => region.PixelArea) > strict.Result.Regions.Sum(region => region.PixelArea));
    }

    private sealed class CpuOptionsFactory : IAnomalibSessionOptionsFactory
    {
        public SessionOptions Create() => new();
    }
}
