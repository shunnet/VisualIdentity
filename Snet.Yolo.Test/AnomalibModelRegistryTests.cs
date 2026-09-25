using System.Security.Cryptography;
using System.IO.Compression;
using Snet.Yolo.Server.Anomalib;
using Snet.Yolo.Tasks.Services;
using Xunit;

namespace Snet.Yolo.Test;

/// <summary>验证 Anomalib 模型只有通过门禁且与原文件匹配时才能注册。</summary>
public sealed class AnomalibModelRegistryTests
{
    /// <summary>注册后的模型仅出现在所属用户列表中，篡改模型后不能重新注册。</summary>
    [Fact]
    public async Task RegisterAsync_RestrictsOwnerAndRejectsModifiedModel()
    {
        var owner = "registry-" + Guid.NewGuid().ToString("N");
        var projectId = Guid.NewGuid().ToString("N");
        var root = AnomalibModelRegistry.ProjectRoot(owner, projectId);
        var artifacts = Path.Combine(root, "anomalib-run-" + Guid.NewGuid().ToString("N"), "artifacts");
        Directory.CreateDirectory(artifacts);
        try
        {
            var onnxPath = Path.Combine(artifacts, "model.onnx");
            var checkpointPath = Path.Combine(artifacts, "model.ckpt");
            var manifestPath = Path.Combine(artifacts, "model.manifest.json");
            var bytes = new byte[] { 1, 2, 3, 4 };
            await File.WriteAllBytesAsync(onnxPath, bytes);
            await File.WriteAllBytesAsync(checkpointPath, bytes);
            await File.WriteAllTextAsync(manifestPath, ValidManifest(Convert.ToHexStringLower(SHA256.HashData(bytes))));
            var artifact = new AnomalibTrainingArtifact
            {
                Owner = owner,
                ProjectId = projectId,
                Model = AnomalibModelKind.Padim,
                OnnxPath = onnxPath,
                CheckpointPath = checkpointPath,
                ManifestPath = manifestPath,
                DatasetSha256 = new string('a', 64),
                Parity = AnomalibParityResult.Parse("""{"status":"passed","sampleCount":2,"normalSampleCount":2,"normalFalsePositiveCount":0,"maxScoreDifference":0,"minimumMaskIou":1,"labelMismatches":0,"errors":[]}"""),
            };
            var registry = new AnomalibModelRegistry();
            await registry.RegisterAsync(artifact, CancellationToken.None);
            Assert.Single(await registry.ListAsync(owner));
            Assert.Empty(await registry.ListAsync(owner + "-other"));
            var registered = Assert.Single(await registry.ListAsync(owner));
            Assert.StartsWith("PaDiM", registered.Name);
            Assert.False(await registry.UpdateAsync(owner + "-other", projectId, registered.RunId, "私有模型", null));
            Assert.True(await registry.UpdateAsync(owner, projectId, registered.RunId, "更新名称", "更新描述"));
            var updated = Assert.Single(await registry.ListAsync(owner));
            Assert.Equal("更新名称", updated.Name);
            Assert.Equal("更新描述", updated.Description);
            Assert.Equal(AnomalibModelKind.Padim, updated.Model);
            using (var package = new MemoryStream())
            {
                await AnomalibModelRegistry.WritePackageAsync(registered, package);
                package.Position = 0;
                using var archive = new ZipArchive(package, ZipArchiveMode.Read, leaveOpen: true);
                Assert.Equal(["model.onnx", "model.manifest.json"], archive.Entries.Select(entry => entry.FullName).ToArray());
            }
            string temporaryPackagePath;
            await using (var download = await AnomalibModelRegistry.OpenPackageDownloadAsync(registered))
            {
                temporaryPackagePath = download.Name;
                Assert.True(download.CanSeek);
                using var archive = new ZipArchive(download, ZipArchiveMode.Read, leaveOpen: true);
                Assert.Equal(["model.onnx", "model.manifest.json"], archive.Entries.Select(entry => entry.FullName).ToArray());
            }
            Assert.False(File.Exists(temporaryPackagePath));
            var normalDirectory = Path.Combine(root, "normal");
            Directory.CreateDirectory(normalDirectory);
            var normalImage = Path.Combine(normalDirectory, "sample.png");
            await File.WriteAllBytesAsync(normalImage, bytes);
            await File.WriteAllBytesAsync(onnxPath, new byte[] { 9, 9, 9 });
            await Assert.ThrowsAsync<InvalidDataException>(() => registry.RegisterAsync(artifact, CancellationToken.None));
            Assert.False(await registry.DeleteAsync(owner + "-other", projectId, registered.RunId));
            Assert.False(await registry.DeleteAsync(owner, projectId, "../other"));
            Assert.True(await registry.DeleteAsync(owner, projectId, registered.RunId));
            Assert.False(Directory.Exists(artifacts));
            Assert.True(File.Exists(normalImage));
            Assert.Empty(await registry.ListAsync(owner));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    /// <summary>拒绝类型错配和伪造的 ONNX 包，失败后不留下可见模型或导入目录。</summary>
    [Fact]
    public async Task ImportAsync_RejectsInvalidPackageAndCleansUp()
    {
        var owner = "registry-import-" + Guid.NewGuid().ToString("N");
        var ownerRoot = Path.Combine(AppContext.BaseDirectory, "train", "anomalib", "users", UserStoragePath.Segment(owner));
        var registry = new AnomalibModelRegistry();
        var modelBytes = new byte[] { 1, 2, 3, 4 };
        try
        {
            using (var wrongType = Package(modelBytes, ValidManifest(Convert.ToHexStringLower(SHA256.HashData(modelBytes)))))
            {
                await Assert.ThrowsAsync<AnomalibModelImportException>(() => registry.ImportAsync(owner, wrongType, "模型 A", "描述", AnomalibModelKind.EfficientAdSmall));
            }
            using (var wrongHash = Package(modelBytes, ValidManifest(new string('a', 64))))
            {
                await Assert.ThrowsAsync<AnomalibModelImportException>(() => registry.ImportAsync(owner, wrongHash, "模型 B", "描述", AnomalibModelKind.Padim));
            }
            using (var invalidOnnx = Package(modelBytes, ValidManifest(Convert.ToHexStringLower(SHA256.HashData(modelBytes)))))
            {
                await Assert.ThrowsAnyAsync<Exception>(() => registry.ImportAsync(owner, invalidOnnx, "模型 C", "描述", AnomalibModelKind.Padim));
            }
            Assert.Empty(await registry.ListAsync(owner));
            if (Directory.Exists(ownerRoot)) { Assert.Empty(Directory.EnumerateFileSystemEntries(ownerRoot)); }
        }
        finally { if (Directory.Exists(ownerRoot)) { Directory.Delete(ownerRoot, recursive: true); } }
    }

    /// <summary>构造只包含 ONNX 和解析清单的模型包。</summary>
    private static MemoryStream Package(byte[] model, string manifest)
    {
        var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            using (var entry = archive.CreateEntry("model.onnx").Open()) { entry.Write(model); }
            using (var writer = new StreamWriter(archive.CreateEntry("model.manifest.json").Open())) { writer.Write(manifest); }
        }
        stream.Position = 0;
        return stream;
    }

    /// <summary>导入有效 ONNX 包后，名称、描述和类型可持久读取，下载与删除仍可使用。</summary>
    [Fact]
    public async Task ImportAsync_ValidPackage_PersistsMetadata()
    {
        var owner = "registry-import-valid-" + Guid.NewGuid().ToString("N");
        var ownerRoot = Path.Combine(AppContext.BaseDirectory, "train", "anomalib", "users", UserStoragePath.Segment(owner));
        var registry = new AnomalibModelRegistry();
        // 由 ONNX helper 生成的最小四输出模型，只有常量节点，不依赖外部权重。
        var modelBytes = Convert.FromBase64String("CAg6uAMKMRIKcHJlZF9zY29yZSIIQ29uc3RhbnQqGQoFdmFsdWUqDQgBEAEiBM3MzD1CAXOgAQQKLhIKcHJlZF9sYWJlbCIIQ29uc3RhbnQqFgoFdmFsdWUqCggBEAkqAQBCAWygAQQKchILYW5vbWFseV9tYXAiCENvbnN0YW50KlkKBXZhbHVlKk0IAQgECAQQASJAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAEIBbaABBApAEglwcmVkX21hc2siCENvbnN0YW50KikKBXZhbHVlKh0IAQgECAQQCSoQAAAAAAAAAAAAAAAAAAAAAEIBcKABBBIEdGVzdFofCgVpbnB1dBIWChQIARIQCgIIAQoCCAMKAgggCgIIIGIYCgpwcmVkX3Njb3JlEgoKCAgBEgQKAggBYhgKCnByZWRfbGFiZWwSCgoICAkSBAoCCAFiIQoLYW5vbWFseV9tYXASEgoQCAESDAoCCAEKAggECgIIBGIfCglwcmVkX21hc2sSEgoQCAkSDAoCCAEKAggECgIIBEIECgAQDQ==");
        try
        {
            using var package = Package(modelBytes, ValidManifest(Convert.ToHexStringLower(SHA256.HashData(modelBytes)), 32));
            var imported = await registry.ImportAsync(owner, package, "  表面检测  ", "工件表面", AnomalibModelKind.Padim);
            var listed = Assert.Single(await registry.ListAsync(owner));
            Assert.Equal("表面检测", listed.Name);
            Assert.Equal("工件表面", listed.Description);
            Assert.Equal(AnomalibModelKind.Padim, listed.Model);
            Assert.Equal(imported.OnnxPath, listed.OnnxPath);
            Assert.True(await registry.DeleteAsync(owner, listed.ProjectId, listed.RunId));
            Assert.Empty(await registry.ListAsync(owner));
        }
        finally { if (Directory.Exists(ownerRoot)) { Directory.Delete(ownerRoot, recursive: true); } }
    }

    /// <summary>生成满足格式约定且摘要由测试内容决定的 Anomalib 清单。</summary>
    private static string ValidManifest(string digest, int size = 256) => $$"""
        {
          "schemaVersion": "1.0",
          "family": "anomalib",
          "anomalibVersion": "2.6.2",
          "algorithm": "padim",
          "modelSha256": "{{digest}}",
          "input": {"name":"input","elementType":"float32","layout":"nchw","colorSpace":"rgb","width":{{size}},"height":{{size}},"resizeMode":"stretch","valueRange":"zeroToOne","normalizationEmbedded":true},
          "outputs": {"predictionScore":"pred_score","predictionLabel":"pred_label","anomalyMap":"anomaly_map","predictionMask":"pred_mask"},
          "postProcessing": {"threshold":0.5,"thresholdSource":"syntheticCalibration"}
        }
        """;
}
