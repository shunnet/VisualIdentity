using System.IO.Compression;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Microsoft.ML.OnnxRuntime;
using SkiaSharp;
using Snet.Yolo.Api.Controllers;
using Snet.Yolo.Api.Model;
using Snet.Yolo.Server.Anomalib;
using Xunit;

namespace Snet.Yolo.Test;

public sealed class AnomalibApiTests
{
    private const string FixtureOnnx = "CAg6jQQKQBIKcHJlZF9zY29yZSIIQ29uc3RhbnQqKAoFdmFsdWUqHAgBEAFCEHByZWRfc2NvcmVfdmFsdWVKBAAAQD+gAQQKPRIKcHJlZF9sYWJlbCIIQ29uc3RhbnQqJQoFdmFsdWUqGQgBEAlCEHByZWRfbGFiZWxfdmFsdWVKAQGgAQQKhAESC2Fub21hbHlfbWFwIghDb25zdGFudCprCgV2YWx1ZSpfCAEIAQgECAQQAUIRYW5vbWFseV9tYXBfdmFsdWVKQAAAAACJiIg9iYgIPs3MTD6JiIg+q6qqPs3MzD7v7u4+iYgIP5qZGT+rqio/vLs7P83MTD/e3V0/7+5uPwAAgD+gAQQKUBIJcHJlZF9tYXNrIghDb25zdGFudCo5CgV2YWx1ZSotCAEIAQgECAQQCUIPcHJlZF9tYXNrX3ZhbHVlShAAAAAAAAEBAAABAQAAAAAAoAEEEhBhbm9tYWxpYl9maXh0dXJlWh8KBWltYWdlEhYKFAgBEhAKAggBCgIIAwoCCCAKAgggYhgKCnByZWRfc2NvcmUSCgoICAESBAoCCAFiGAoKcHJlZF9sYWJlbBIKCggICRIECgIIAWIlCgthbm9tYWx5X21hcBIWChQIARIQCgIIAQoCCAEKAggECgIIBGIjCglwcmVkX21hc2sSFgoUCAkSEAoCCAEKAggBCgIIBAoCCARCBAoAEA0=";

    [Fact]
    public async Task ModelEndpoints_RejectUnknownOrInvalidModels()
    {
        var controller = new AnomalibController(new AnomalibModelRegistry(),
            new AnomalibOnnxInference(new UnusedOptionsFactory()), Options.Create(new ConfigModel()))
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() },
        };

        Assert.IsType<NotFoundResult>(await controller.GetAsync("../invalid", "anomalib-run-missing"));
        Assert.IsType<NotFoundResult>(await controller.UpdateAsync("../invalid", "anomalib-run-missing",
            new UpdateAnomalibModelRequest { Name = "name" }));
        Assert.IsType<NotFoundResult>(await controller.DeleteAsync("../invalid", "anomalib-run-missing"));
        Assert.IsType<NotFoundResult>(await controller.DownloadAsync("../invalid", "anomalib-run-missing"));
        Assert.IsType<NotFoundResult>(await controller.IdentifyAsync("../invalid", "anomalib-run-missing", null!));
        Assert.IsType<BadRequestObjectResult>(await controller.ImportAsync(null!, "test", null, AnomalibModelKind.Padim));
    }

    [Fact]
    public async Task ImportedOnnx_CanBeManagedAndRecognizeAnImage()
    {
        var storageRoot = Path.Combine(Path.GetTempPath(), "snet-anomalib-api-test-" + Guid.NewGuid().ToString("N"));
        var registry = new AnomalibModelRegistry(storageRoot);
        using var inference = new AnomalibOnnxInference(new CpuOptionsFactory());
        var controller = new AnomalibController(registry, inference, Options.Create(new ConfigModel()))
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() },
        };
        var bytes = Convert.FromBase64String(FixtureOnnx);
        var name = "api-fixture-" + Guid.NewGuid().ToString("N");
        var manifest = AnomalibManifestSerializer.Serialize(new AnomalibModelManifest
        {
            SchemaVersion = "1.0",
            Family = "anomalib",
            AnomalibVersion = "test",
            Algorithm = AnomalibAlgorithm.Padim,
            ModelSha256 = Convert.ToHexStringLower(SHA256.HashData(bytes)),
            Input = new AnomalibInputContract
            {
                Name = "image", ElementType = OnnxTensorElementType.Float32,
                Layout = AnomalibTensorLayout.Nchw, ColorSpace = AnomalibColorSpace.Rgb,
                Width = 32, Height = 32, ResizeMode = AnomalibResizeMode.Stretch,
                ValueRange = AnomalibValueRange.ZeroToOne, NormalizationEmbedded = true,
            },
            Outputs = new AnomalibOutputNames
            {
                PredictionScore = "pred_score", PredictionLabel = "pred_label",
                AnomalyMap = "anomaly_map", PredictionMask = "pred_mask",
            },
            PostProcessing = new AnomalibPostProcessingContract
            {
                Threshold = 0.5f, ThresholdSource = AnomalibThresholdSource.Manual,
            },
        });
        RegisteredAnomalibModel? model = null;
        try
        {
            using var package = new MemoryStream();
            using (var zip = new ZipArchive(package, ZipArchiveMode.Create, leaveOpen: true))
            {
                using (var entry = zip.CreateEntry("model.onnx").Open()) { await entry.WriteAsync(bytes); }
                using (var entry = zip.CreateEntry("model.manifest.json").Open())
                using (var writer = new StreamWriter(entry)) { await writer.WriteAsync(manifest); }
            }
            package.Position = 0;
            var upload = new FormFile(package, 0, package.Length, "file", "fixture.zip");
            Assert.IsType<OkObjectResult>(await controller.ImportAsync(upload, name, "fixture", AnomalibModelKind.Padim));
            model = Assert.Single(await registry.ListAsync("snet"), item => item.Name == name);
            Assert.DoesNotContain(await new AnomalibModelRegistry().ListAsync("snet"), item => item.RunId == model.RunId);
            Assert.IsType<OkObjectResult>(await controller.GetAsync(model.ProjectId, model.RunId));
            Assert.IsType<OkObjectResult>(await controller.UpdateAsync(model.ProjectId, model.RunId,
                new UpdateAnomalibModelRequest { Name = name + "-updated", Description = "edited" }));
            var download = Assert.IsType<FileStreamResult>(await controller.DownloadAsync(model.ProjectId, model.RunId));
            await download.FileStream.DisposeAsync();

            using var bitmap = new SKBitmap(32, 32);
            bitmap.Erase(SKColors.White);
            using var image = SKImage.FromBitmap(bitmap);
            using var encoded = image.Encode(SKEncodedImageFormat.Png, 90);
            using var imageStream = new MemoryStream(encoded.ToArray());
            var imageUpload = new FormFile(imageStream, 0, imageStream.Length, "file", "image.png");
            var response = Assert.IsType<OkObjectResult>(await controller.IdentifyAsync(model.ProjectId, model.RunId, imageUpload, true));
            var result = Assert.IsType<AnomalibInferenceOutput>(response.Value);
            Assert.Equal(0.75f, result.Result.ImageScore);
            Assert.True(result.Result.IsAnomalous);
            Assert.Single(result.Result.Regions);
            Assert.StartsWith("data:image/png;base64,", result.HeatmapDataUrl);

            inference.Release(model.OnnxPath);
            Assert.IsType<OkResult>(await controller.DeleteAsync(model.ProjectId, model.RunId));
            model = null;
        }
        finally
        {
            if (model is not null)
            {
                inference.Release(model.OnnxPath);
                await registry.DeleteAsync("snet", model.ProjectId, model.RunId);
            }
            if (Directory.Exists(storageRoot)) { Directory.Delete(storageRoot, recursive: true); }
        }
    }

    private sealed class UnusedOptionsFactory : IAnomalibSessionOptionsFactory
    {
        public SessionOptions Create() => throw new NotSupportedException();
    }

    private sealed class CpuOptionsFactory : IAnomalibSessionOptionsFactory
    {
        public SessionOptions Create() => new();
    }
}
