using System.IO.Compression;
using Snet.Yolo.Server.anomalib;
using Snet.Yolo.Tasks.Services;
using Xunit;

namespace Snet.Yolo.Test;

/// <summary>未通过校准的导出仅由所有者下载，手动导入仍走现有模型契约检查。</summary>
public sealed class AnomalibTrainingDownloadTests
{
    /// <summary>下载不产生注册标记，用户主动导入后才出现在验证模型列表。</summary>
    [Fact]
    public async Task DownloadedExport_CanBeExplicitlyImportedWithoutAutomaticRegistration()
    {
        var root = Path.Combine(Path.GetTempPath(), "training-download-" + Guid.NewGuid().ToString("N"));
        var work = Path.Combine(root, "alice", "project");
        Directory.CreateDirectory(work);
        try
        {
            var runner = new CalibrationArtifactRunner(AnomalibCalibrationExportTests.FailedReport);
            var result = await new AnomalibTrainingService(runner, runner).TrainAsync(AnomalibCalibrationExportTests.CreateRequest(work), null, TestContext.Current.CancellationToken);
            Assert.NotNull(result.Artifact);
            var run = Path.GetFileName(Path.GetDirectoryName(Path.GetDirectoryName(result.Artifact.OnnxPath))!);
            var registry = new AnomalibModelRegistry(root);
            Assert.Empty(await registry.ListAsync("alice", TestContext.Current.CancellationToken));
            Assert.Null(await registry.OpenTrainingPackageDownloadAsync("bob", "project", run, TestContext.Current.CancellationToken));
            Assert.Null(await registry.OpenTrainingPackageDownloadAsync("alice", "../project", run, TestContext.Current.CancellationToken));
            Assert.Null(await registry.OpenTrainingPackageDownloadAsync("alice", "project", "../run", TestContext.Current.CancellationToken));
            using var saved = new MemoryStream();
            await using (var download = await registry.OpenTrainingPackageDownloadAsync("alice", "project", run, TestContext.Current.CancellationToken))
            {
                Assert.NotNull(download);
                await download.CopyToAsync(saved, TestContext.Current.CancellationToken);
            }
            saved.Position = 0;
            using (var zip = new ZipArchive(saved, ZipArchiveMode.Read, leaveOpen: true))
            {
                Assert.Equal(new[] { "model.manifest.json", "model.onnx" }, zip.Entries.Select(entry => entry.FullName).Order());
            }
            Assert.Empty(Directory.GetFiles(Path.GetDirectoryName(result.Artifact.OnnxPath)!, ".download-*.zip"));
            Assert.Empty(await registry.ListAsync("alice", TestContext.Current.CancellationToken));
            await using (var report = await registry.OpenTrainingReportDownloadAsync("alice", "project", run, TestContext.Current.CancellationToken))
            {
                Assert.NotNull(report);
                using var reader = new StreamReader(report);
                Assert.Contains("one.png", await reader.ReadToEndAsync(TestContext.Current.CancellationToken));
            }
            saved.Position = 0;
            var imported = await registry.ImportAsync("alice", saved, "手动检查的模型", "校准未通过，人工复核", AnomalibModelKind.Padim,
                cancellationToken: TestContext.Current.CancellationToken);
            Assert.Equal(imported.RunId, Assert.Single(await registry.ListAsync("alice", TestContext.Current.CancellationToken)).RunId);
            Assert.Empty(await registry.ListAsync("bob", TestContext.Current.CancellationToken));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    /// <summary>完整导出标志缺失或模型被修改时，不能下载半成品或损坏模型。</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Download_RejectsIncompleteOrModifiedArtifacts(bool tamper)
    {
        var root = Path.Combine(Path.GetTempPath(), "training-invalid-" + Guid.NewGuid().ToString("N"));
        var work = Path.Combine(root, "alice", "project");
        Directory.CreateDirectory(work);
        try
        {
            var runner = new CalibrationArtifactRunner(AnomalibCalibrationExportTests.FailedReport);
            var result = await new AnomalibTrainingService(runner, runner).TrainAsync(AnomalibCalibrationExportTests.CreateRequest(work), null, TestContext.Current.CancellationToken);
            var artifact = Assert.IsType<AnomalibTrainingArtifact>(result.Artifact);
            var directory = Path.GetDirectoryName(Path.GetDirectoryName(artifact.OnnxPath))!;
            var run = Path.GetFileName(directory);
            var registry = new AnomalibModelRegistry(root);
            if (tamper)
            {
                await File.WriteAllBytesAsync(artifact.OnnxPath, [9], TestContext.Current.CancellationToken);
                await Assert.ThrowsAsync<InvalidDataException>(() => registry.OpenTrainingPackageDownloadAsync("alice", "project", run, TestContext.Current.CancellationToken));
            }
            else
            {
                await File.WriteAllTextAsync(Path.Combine(directory, "result.json"), AnomalibCalibrationExportTests.FailedReport.Replace("\"exportSucceeded\":true", "\"exportSucceeded\":false"), TestContext.Current.CancellationToken);
                Assert.Null(await registry.OpenTrainingPackageDownloadAsync("alice", "project", run, TestContext.Current.CancellationToken));
            }
            Assert.Empty(await registry.ListAsync("alice", TestContext.Current.CancellationToken));
        }
        finally { Directory.Delete(root, recursive: true); }
    }
}
