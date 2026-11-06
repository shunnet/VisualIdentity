using System.IO.Compression;
using Snet.Yolo.Server.anomalib;
using Xunit;

namespace Snet.Yolo.Test;

/// <summary>大于旧 1 GiB 限额的 PatchCore 包仍须流式进入完整性检查，不能误报文件格式错误。</summary>
public sealed class AnomalibLargePackageTests
{
    /// <summary>用可压缩的大文件检查解包边界和摘要验证，全程不分配模型大小的托管数组。</summary>
    /// <param name="modelMiB">未压缩模型大小。</param>
    /// <param name="expectedError">应由大小或摘要检查返回的具体错误。</param>
    [Theory]
    [InlineData(1091, "AnomalibPackageHashMismatch")]
    [InlineData(2049, "AnomalibPackageTooLarge")]
    public async Task ImportAsync_LargeModels_KeepSizeAndDigestGuards(int modelMiB, string expectedError)
    {
        var root = Path.Combine(Path.GetTempPath(), "anomalib-large-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var token = TestContext.Current.CancellationToken;
        try
        {
            var package = Path.Combine(root, "model.zip");
            await using (var file = new FileStream(package, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            using (var zip = new ZipArchive(file, ZipArchiveMode.Create, leaveOpen: true))
            {
                await using (var model = zip.CreateEntry("model.onnx", CompressionLevel.Fastest).Open())
                {
                    var block = new byte[1024 * 1024];
                    for (var i = 0; i < modelMiB; i++) { await model.WriteAsync(block, token); }
                }
                await using var manifest = zip.CreateEntry("model.manifest.json").Open();
                var text = """
                    {"schemaVersion":"1.0","family":"anomalib","anomalibVersion":"2.6.2","algorithm":"patchCore","modelSha256":"DIGEST",
                     "input":{"name":"input","elementType":"float32","layout":"nchw","colorSpace":"rgb","width":960,"height":960,"resizeMode":"stretch","valueRange":"zeroToOne","normalizationEmbedded":true},
                     "outputs":{"predictionScore":"pred_score","predictionLabel":"pred_label","anomalyMap":"anomaly_map","predictionMask":"pred_mask"},
                     "postProcessing":{"threshold":0.5,"thresholdSource":"manual"}}
                    """.Replace("DIGEST", new string('a', 64), StringComparison.Ordinal);
                await manifest.WriteAsync(System.Text.Encoding.UTF8.GetBytes(text), token);
            }
            var registry = new AnomalibModelRegistry(Path.Combine(root, "models"));
            await using var input = File.OpenRead(package);
            var error = await Assert.ThrowsAsync<AnomalibModelImportException>(() =>
                registry.ImportAsync("alice", input, "大模型", "大于 1 GiB 的模型仍检查摘要", AnomalibModelKind.PatchCore, cancellationToken: token));
            Assert.Equal(expectedError, error.ResourceKey);
            Assert.Empty(await registry.ListAsync("alice", token));
            Assert.Empty(Directory.EnumerateFiles(Path.Combine(root, "models"), "*", SearchOption.AllDirectories));
        }
        finally { Directory.Delete(root, recursive: true); }
    }
}
