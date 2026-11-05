using System.Reflection;
using System.Security.Cryptography;
using SkiaSharp;
using Snet.Yolo.Server.anomalib;
using Xunit;

namespace Snet.Yolo.Test;

/// <summary>设备切换必须替换缓存会话，同时尊重旧会话的活动租约。</summary>
public sealed class AnomalibHardwareCacheTests
{
    private const string Model = "CAg6uAMKMRIKcHJlZF9zY29yZSIIQ29uc3RhbnQqGQoFdmFsdWUqDQgBEAEiBM3MzD1CAXOgAQQKLhIKcHJlZF9sYWJlbCIIQ29uc3RhbnQqFgoFdmFsdWUqCggBEAkqAQBCAWygAQQKchILYW5vbWFseV9tYXAiCENvbnN0YW50KlkKBXZhbHVlKk0IAQgECAQQASJAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAEIBbaABBApAEglwcmVkX21hc2siCENvbnN0YW50KikKBXZhbHVlKh0IAQgECAQQCSoQAAAAAAAAAAAAAAAAAAAAAEIBcKABBBIEdGVzdFofCgVpbnB1dBIWChQIARIQCgIIAQoCCAMKAgggCgIIIGIYCgpwcmVkX3Njb3JlEgoKCAgBEgQKAggBYhgKCnByZWRfbGFiZWwSCgoICAkSBAoCCAFiIQoLYW5vbWFseV9tYXASEgoQCAESDAoCCAEKAggECgIIBGIfCglwcmVkX21hc2sSEgoQCAkSDAoCCAEKAggECgIIBEIECgAQDQ==";

    /// <summary>同设备复用一次会话，配置标识变化后必须创建新的原生会话。</summary>
    [Fact]
    public async Task ChangedDevice_ReplacesCachedSession()
    {
        var files = CreateFixture();
        try
        {
            var factory = new SwitchingAnomalibOptionsFactory();
            using var inference = new AnomalibOnnxInference(factory);
            await inference.IdentifyAsync(files.Model, files.Manifest, files.Image, TestContext.Current.CancellationToken);
            await inference.IdentifyAsync(files.Model, files.Manifest, files.Image, TestContext.Current.CancellationToken);
            Assert.Equal(1, factory.Created);
            factory.CacheKey = "device-b";
            await inference.IdentifyAsync(files.Model, files.Manifest, files.Image, TestContext.Current.CancellationToken);
            Assert.Equal(2, factory.Created);
        }
        finally { Directory.Delete(files.Directory, true); }
    }

    /// <summary>删除模型必须等待当前设备和切换前设备的所有活动租约。</summary>
    [Fact]
    public async Task ReleaseAsync_WaitsForRetiredAndCurrentDeviceLeases()
    {
        var files = CreateFixture();
        try
        {
            var factory = new SwitchingAnomalibOptionsFactory();
            using var inference = new AnomalibOnnxInference(factory);
            var acquire = typeof(AnomalibOnnxInference).GetMethod("Acquire", BindingFlags.Instance | BindingFlags.NonPublic)!;
            using var first = (IDisposable)acquire.Invoke(inference, [files.Model, files.Manifest])!;
            factory.CacheKey = "device-b";
            using var second = (IDisposable)acquire.Invoke(inference, [files.Model, files.Manifest])!;
            Assert.Equal(2, factory.Created);
            var release = inference.ReleaseAsync(files.Model, TestContext.Current.CancellationToken);
            Assert.False(release.IsCompleted);
            second.Dispose();
            Assert.False(release.IsCompleted);
            first.Dispose();
            await release.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        }
        finally { Directory.Delete(files.Directory, true); }
    }

    /// <summary>生成独立的正常图、常量 ONNX 模型和一致性清单。</summary>
    /// <returns>测试文件路径。</returns>
    private static (string Directory, string Model, string Manifest, string Image) CreateFixture()
    {
        var directory = Path.Combine(Path.GetTempPath(), "anomalib-device-cache-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var bytes = Convert.FromBase64String(Model);
        var model = Path.Combine(directory, "model.onnx");
        var manifest = Path.Combine(directory, "model.manifest.json");
        var image = Path.Combine(directory, "input.png");
        File.WriteAllBytes(model, bytes);
        File.WriteAllText(manifest, $$"""
            {"schemaVersion":"1.0","family":"anomalib","anomalibVersion":"2.6.2","algorithm":"padim","modelSha256":"{{Convert.ToHexStringLower(SHA256.HashData(bytes))}}",
             "input":{"name":"input","elementType":"float32","layout":"nchw","colorSpace":"rgb","width":32,"height":32,"resizeMode":"stretch","valueRange":"zeroToOne","normalizationEmbedded":true},
             "outputs":{"predictionScore":"pred_score","predictionLabel":"pred_label","anomalyMap":"anomaly_map","predictionMask":"pred_mask"},"postProcessing":{"threshold":0.5,"thresholdSource":"syntheticCalibration"} }
            """);
        using var bitmap = new SKBitmap(32, 32);
        bitmap.Erase(SKColors.Gray);
        using var encoded = bitmap.Encode(SKEncodedImageFormat.Png, 100);
        File.WriteAllBytes(image, encoded.ToArray());
        return (directory, model, manifest, image);
    }
}
