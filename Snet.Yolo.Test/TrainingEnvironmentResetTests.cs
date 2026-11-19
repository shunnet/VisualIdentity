using Snet.Yolo.Server.models;
using Snet.Yolo.Tasks.Core.Training;
using Snet.Yolo.Tasks.Services;
using Microsoft.Extensions.Logging.Abstractions;
using System.Reflection;
using Xunit;

namespace Snet.Yolo.Test;

/// <summary>重装只修改算法环境，选择最新兼容依赖，并可安全取消。</summary>
public sealed class TrainingEnvironmentResetTests
{
    [Theory]
    [InlineData(8.6)]
    [InlineData(8.9)]
    [InlineData(12.0)]
    public void YoloReset_InstallsLatestStableTorchWithinCuda128(double computeCapability)
    {
        var gpu = new GpuInfo("NVIDIA GPU", computeCapability.ToString(System.Globalization.CultureInfo.InvariantCulture), "595.99.02", 12288, null);
        var steps = TrainingEnvironmentResetOperation.CreatePlan(ProjectKind.Yolo, Path.GetTempPath(), new("python", []), OsKind.Windows, gpu);
        var install = Assert.Single(steps, step => step.Kind == SetupStepKind.PipInstallTorch);
        Assert.Contains("torch", install.Command.ArgumentList);
        Assert.DoesNotContain(install.Command.ArgumentList, arg => arg.StartsWith("torch==", StringComparison.Ordinal));
        Assert.Contains("https://download.pytorch.org/whl/cu128", install.Command.ArgumentList);
    }

    [Fact]
    public void AnomalibReset_PreservesAdaptedVersionsAndDedicatedEnvironment()
    {
        var steps = TrainingEnvironmentResetOperation.CreatePlan(ProjectKind.Anomalib, Path.GetTempPath(), new("python", []), OsKind.Windows, null);
        var packages = steps.SelectMany(step => step.Command.ArgumentList).ToArray();
        Assert.Contains("anomalib==2.7.1", packages);
        Assert.Contains("torch==2.11.0", packages);
        Assert.Contains("torchvision==0.26.0", packages);
        Assert.Contains("onnxruntime==1.31.1", packages);
        var create = Assert.Single(steps, step => step.Kind == SetupStepKind.RecreateVenv);
        Assert.EndsWith(Path.Combine("train", "anomalib", ".env"), create.Command.ArgumentList.Last());
        Assert.DoesNotContain(steps, step => step.Command.ArgumentList.Contains("train"));
    }

    [Fact]
    public async Task ResetCancelledBeforeSetup_CompletesWithoutStartingInstallation()
    {
        var operation = new TrainingEnvironmentResetOperation("alice", "project");
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await operation.RunAsync(ProjectKind.Yolo, "cpu", null, null, cancellation.Token);
        var status = operation.Snapshot();
        Assert.False(status.IsActive);
        Assert.False(status.Succeeded);
        Assert.Single(status.Logs);
        Assert.Contains(status.Logs, line => line.Contains("已取消", StringComparison.Ordinal));
    }

    /// <summary>下载失败时恢复原环境，也能处理上次进程中断留下的半成品。</summary>
    [Theory]
    [InlineData(ProjectKind.Anomalib, false)]
    [InlineData(ProjectKind.Anomalib, true)]
    [InlineData(ProjectKind.Yolo, false)]
    [InlineData(ProjectKind.Yolo, true)]
    public async Task ResetFailedDuringSetup_RestoresOriginalEnvironment(ProjectKind kind, bool interrupted)
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var venv = Path.Combine(root, ".env");
        var previous = venv + ".previous";
        var original = interrupted ? previous : venv;
        Directory.CreateDirectory(original);
        try
        {
            File.WriteAllText(Path.Combine(original, "original.txt"), "ready");
            if (interrupted)
            {
                Directory.CreateDirectory(venv);
                File.WriteAllText(Path.Combine(venv, "partial.txt"), "incomplete");
            }

            var logs = new List<string>();
            var steps = new[]
            {
                new SetupStep(SetupStepKind.PipInstallTorch, "下载 PyTorch", new SetupCommand(Path.Combine(root, "missing-python"), [], true)),
            };
            var error = await Assert.ThrowsAsync<InvalidOperationException>(() => TrainingEnvironmentInstaller.InstallAsync(kind,
                venv, "missing-python", steps, root, "cpu", [], null, null, logs.Add, TestContext.Current.CancellationToken));
            Assert.Contains("下载 PyTorch失败", error.Message);
            Assert.Contains(logs, line => line.Contains("已恢复", StringComparison.Ordinal));
            Assert.Equal("ready", File.ReadAllText(Path.Combine(venv, "original.txt")));
            Assert.False(File.Exists(Path.Combine(venv, "partial.txt")));
            Assert.False(Directory.Exists(previous));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public async Task YoloReset_RejectsWhileAnotherTrainingOwnsSharedEnvironment()
    {
        await using var service = new TrainingService(null!, null!, NullLogger<TrainingService>.Instance);
        typeof(TrainingService).GetField("_running", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(service, 1);
        Assert.False(await service.StartEnvironmentResetAsync("alice", "project", "cpu"));
        Assert.Null(service.GetEnvironmentResetStatus("alice", "project"));
    }

    [Fact]
    public async Task AnomalibReset_RejectsDuplicateAndIsolatesOwnerLogs()
    {
        await using var service = new AnomalibWorkflowService(null!, null!);
        var operation = new TrainingEnvironmentResetOperation("alice", "project");
        typeof(AnomalibWorkflowService).GetField("_environmentReset", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(service, operation);
        Assert.False(await service.StartEnvironmentResetAsync("alice", "project", "cpu"));
        Assert.True(service.GetEnvironmentResetStatus("alice", "project")!.IsActive);
        Assert.Null(service.GetEnvironmentResetStatus("bob", "project"));
        Assert.Null(service.GetEnvironmentResetStatus("alice", "other-project"));
    }
}
