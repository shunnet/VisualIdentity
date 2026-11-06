using System.Text.Json;
using Snet.Yolo.Server.anomalib;
using Snet.Yolo.Tasks.Services;
using Xunit;

namespace Snet.Yolo.Test;

/// <summary>校准未通过时保留导出、保持真实报告且不自动注册的回归。</summary>
public sealed class AnomalibCalibrationExportTests
{
    /// <summary>包含一张误报和一张通过的完整导出报告。</summary>
    internal const string FailedReport = """
        {"status":"failed","exportSucceeded":true,"totalSampleCount":2,"sampleCount":2,"normalSampleCount":2,"normalFalsePositiveCount":1,
         "maxScoreDifference":0,"minimumMaskIou":1,"labelMismatches":0,"errors":[],
         "sampleResults":[
          {"index":1,"fileName":"one.png","passed":false,"torchScore":0.8,"onnxScore":0.8,"torchLabel":true,"onnxLabel":true,"scoreDifference":0,"maskIou":1,"imageThreshold":0.5,"message":"正常图片被判为异常"},
          {"index":2,"fileName":"two.png","passed":true,"torchScore":0.1,"onnxScore":0.1,"torchLabel":false,"onnxLabel":false,"scoreDifference":0,"maskIou":1,"imageThreshold":0.5,"message":""}]}
        """;

    /// <summary>校准失败属于可查看的导出结果，不应删除模型或伪装成已通过。</summary>
    [Fact]
    public async Task TrainingService_PreservesExportAndDetailedReportWithoutRegistration()
    {
        var root = Path.Combine(Path.GetTempPath(), "calibration-export-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var runner = new CalibrationArtifactRunner(FailedReport);
            AnomalibTrainingStatus? status = null;
            var result = await new AnomalibTrainingService(runner, runner).TrainAsync(CreateRequest(root), x => status = x, TestContext.Current.CancellationToken);
            Assert.False(result.Succeeded);
            Assert.NotNull(result.Artifact);
            Assert.False(result.Parity.Passed);
            Assert.Equal(0, runner.RegistrationCount);
            Assert.Equal("CalibrationFailed", status!.Phase.ToString());
            Assert.Equal(100, status.StagePercent);
            Assert.False(status.IsActive);
            Assert.True(File.Exists(result.Artifact.OnnxPath));
            Assert.True(File.Exists(result.Artifact.ManifestPath));
            Assert.True(File.Exists(result.Artifact.CheckpointPath));
            var runRoot = Path.GetDirectoryName(Path.GetDirectoryName(result.Artifact.OnnxPath))!;
            Assert.True(File.Exists(Path.Combine(runRoot, "result.json")));
            Assert.False(Directory.Exists(Path.Combine(runRoot, "dataset")));
            var json = JsonSerializer.SerializeToElement(status.Clone());
            Assert.Equal(2, json.GetProperty("Calibration").GetProperty("totalSampleCount").GetInt32());
            Assert.NotEmpty(json.GetProperty("ExportRunId").GetString()!);
            Assert.DoesNotContain(Directory.EnumerateFiles(root, "registered.json", SearchOption.AllDirectories), _ => true);
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    /// <summary>进程真正失败时，即使曾写出导出标志，也不能保留为可下载成功结果。</summary>
    [Fact]
    public async Task NonzeroProcessExit_DoesNotExposePartialExport()
    {
        var root = Path.Combine(Path.GetTempPath(), "calibration-fatal-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var runner = new CalibrationArtifactRunner(FailedReport, 2);
            var result = await new AnomalibTrainingService(runner, runner).TrainAsync(CreateRequest(root), null, TestContext.Current.CancellationToken);
            Assert.False(result.Succeeded);
            Assert.Null(result.Artifact);
            Assert.Empty(Directory.GetDirectories(root, "anomalib-run-*"));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    /// <summary>构造 20 张内容不同的输入占位文件，测试服务编排而不执行 Python 训练。</summary>
    /// <param name="root">隔离工作目录。</param>
    /// <param name="owner">训练产物的测试所有者。</param>
    /// <param name="projectId">测试工程标识。</param>
    /// <returns>测试用训练请求。</returns>
    internal static AnomalibTrainingRequest CreateRequest(string root, string owner = "alice", string projectId = "project")
    {
        var paths = Enumerable.Range(0, 20).Select(i => Path.Combine(root, $"{i}.png")).ToArray();
        for (var i = 0; i < paths.Length; i++) { File.WriteAllBytes(paths[i], [(byte)i, 1, 2]); }
        return new() { Owner = owner, ProjectId = projectId, WorkingDirectory = root, PythonExecutable = "python", Images = paths,
            Options = new() { Model = AnomalibModelKind.Padim, Device = "cpu", ImageSize = 256 } };
    }
}
