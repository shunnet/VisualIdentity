using SkiaSharp;
using Snet.VisualIdentity.JointVerificationDemo.Configuration;
using Snet.VisualIdentity.JointVerificationDemo.Inference;
using Snet.VisualIdentity.JointVerificationDemo.Output;
using Snet.Yolo.Server.anomalib;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using Xunit;

namespace Snet.Yolo.Test;

/// <summary>控制台 Demo 的参数、路径、原生 Anomalib 会话及输出文件回归验证。</summary>
public sealed class JointVerificationDemoTests
{
    [Theory]
    [InlineData(-0.1)]
    [InlineData(1.1)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void YoloOptions_RejectInvalidThresholds(double value)
        => Assert.Throws<InvalidDataException>(() => new YoloOptions { Confidence = value }.Validate());

    [Fact]
    public async Task Configuration_ResolvesPathsRelativeToConfig_NotWorkingDirectory()
    {
        var root = Path.Combine(Path.GetTempPath(), "joint-demo-config-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            await File.WriteAllBytesAsync(Path.Combine(root, "image.png"), [1]);
            await File.WriteAllBytesAsync(Path.Combine(root, "yolo.onnx"), [1]);
            var path = Path.Combine(root, "demo.json");
            await File.WriteAllTextAsync(path, """
                { "ImagePath": "image.png", "Mode": "YoloOnly", "Yolo": { "OnnxPath": "yolo.onnx", "Confidence": 0.7 }, "IncludeHeatmap": false }
                """);
            var options = await DemoOptions.LoadAsync(path);
            Assert.Equal(Path.Combine(root, "image.png"), options.ImagePath);
            Assert.Equal(Path.Combine(root, "yolo.onnx"), options.Yolo.OnnxPath);
            Assert.Equal(0.7, options.Yolo.Confidence);
            Assert.False(options.IncludeHeatmap);
            Assert.Equal(JointValidationMode.YoloOnly, options.Mode);
            await File.WriteAllTextAsync(path, """{ "Anomalib": null }""");
            await Assert.ThrowsAsync<InvalidDataException>(() => DemoOptions.LoadAsync(path));
        }
        finally { Directory.Delete(root, true); }
    }

    /// <summary>用已有有效 ONNX 固件执行真实 CPU 推理，重复调用后保存和解析结果图。</summary>
    [Fact]
    public async Task Engine_ReusesNativeAnomalibSession_AndWritesDecodableResults()
    {
        var root = Path.Combine(Path.GetTempPath(), "joint-demo-native-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        string? saved = null;
        try
        {
            // 复用 API 原生测试的常量图，而非伪造成功的推理返回值。
            var fixture = (string)typeof(AnomalibApiTests).GetField("FixtureOnnx", BindingFlags.Static | BindingFlags.NonPublic)!.GetRawConstantValue()!;
            var bytes = Convert.FromBase64String(fixture);
            var modelPath = Path.Combine(root, "model.onnx");
            var manifestPath = Path.Combine(root, "model.manifest.json");
            var imagePath = Path.Combine(root, "image.png");
            await File.WriteAllBytesAsync(modelPath, bytes);
            await File.WriteAllTextAsync(manifestPath, AnomalibManifestSerializer.Serialize(new AnomalibModelManifest
            {
                SchemaVersion = "1.0",
                Family = "anomalib",
                AnomalibVersion = "test",
                Algorithm = AnomalibAlgorithm.Padim,
                ModelSha256 = Convert.ToHexStringLower(SHA256.HashData(bytes)),
                Input = new()
                {
                    Name = "image",
                    ElementType = OnnxTensorElementType.Float32,
                    Layout = AnomalibTensorLayout.Nchw,
                    ColorSpace = AnomalibColorSpace.Rgb,
                    Width = 32,
                    Height = 32,
                    ResizeMode = AnomalibResizeMode.Stretch,
                    ValueRange = AnomalibValueRange.ZeroToOne,
                    NormalizationEmbedded = true
                },
                Outputs = new() { PredictionScore = "pred_score", PredictionLabel = "pred_label", AnomalyMap = "anomaly_map", PredictionMask = "pred_mask" },
                PostProcessing = new() { Threshold = 0.5f, ThresholdSource = AnomalibThresholdSource.Manual },
            }));
            using (var image = new SKBitmap(32, 32))
            {
                image.Erase(SKColors.Black);
                using var data = image.Encode(SKEncodedImageFormat.Png, 100);
                await File.WriteAllBytesAsync(imagePath, data.ToArray());
            }
            // 默认是仅 Anomalib 的便携测试；显式提供模型时补跑完整联合 CPU 链路。
            var yoloPath = Environment.GetEnvironmentVariable("SNET_DEMO_TEST_YOLO");
            var options = new DemoOptions
            {
                Anomalib = new() { OnnxPath = modelPath, ManifestPath = manifestPath, PixelThreshold = 0.5f, MinimumArea = 1 },
                Yolo = new() { OnnxPath = yoloPath ?? "unused.onnx" },
                ImagePath = imagePath,
                Mode = yoloPath is null ? JointValidationMode.AnomalibOnly : JointValidationMode.Joint,
            };
            await using var engine = new JointVerificationEngine(options);
            var stages = new List<JointValidationStage>();
            var first = await engine.IdentifyAsync(imagePath, stages.Add);
            var second = await engine.IdentifyAsync(imagePath);
            Assert.True(first.Anomalib!.Result.IsAnomalous);
            Assert.Equal(first.Anomalib.Result.ImageScore, second.Anomalib!.Result.ImageScore);
            Assert.NotEmpty(first.Matches.Regions);
            Assert.Equal(yoloPath is not null, first.YoloExecuted);
            Assert.Contains(JointValidationStage.AnomalibCompleted, stages);
            if (yoloPath is not null) { Assert.Contains(JointValidationStage.YoloCompleted, stages); }
            using var writer = new ResultWriter();
            saved = await writer.SaveAsync(imagePath, second, 123);
            Assert.StartsWith(Path.Combine(AppContext.BaseDirectory, "result") + Path.DirectorySeparatorChar, saved);
            using var annotated = SKBitmap.Decode(Path.Combine(saved, "annotated.png"));
            using var heatmap = SKBitmap.Decode(Path.Combine(saved, "heatmap.png"));
            Assert.Equal(32, annotated.Width);
            Assert.Equal(4, heatmap.Width);
            using var json = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(saved, "result.json")));
            Assert.Equal(123, json.RootElement.GetProperty("TotalMilliseconds").GetInt64());
            Assert.Equal(options.Mode.ToString(), json.RootElement.GetProperty("Mode").GetString());
            using var cancelled = new CancellationTokenSource();
            cancelled.Cancel();
            var resultRoot = Path.Combine(AppContext.BaseDirectory, "result");
            var directoriesBeforeCancel = Directory.GetDirectories(resultRoot).Order().ToArray();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => writer.SaveAsync(imagePath, second, 123, cancelled.Token));
            Assert.Equal(directoriesBeforeCancel, Directory.GetDirectories(resultRoot).Order().ToArray());
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => engine.IdentifyAsync(imagePath, token: cancelled.Token));
            await engine.DisposeAsync();
            await Assert.ThrowsAsync<ObjectDisposedException>(() => engine.IdentifyAsync(imagePath));
        }
        finally
        {
            if (saved is not null) { Directory.Delete(saved, true); }
            Directory.Delete(root, true);
        }
    }
}
