using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Snet.Yolo.Server.anomalib;
using Snet.Yolo.Tasks.Components.Pages;
using Snet.Yolo.Tasks.Components.Shell;
using Snet.Yolo.Tasks.Core.Models;
using Snet.Yolo.Tasks.Core.Training;
using Snet.Yolo.Tasks.Services;
using System.Reflection;
using Xunit;

namespace Snet.Yolo.Test;

/// <summary>危险操作在请求和取消阶段不能修改数据或中断训练。</summary>
public sealed class DestructiveConfirmationTests
{
    private const BindingFlags Members = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
    private static object? Call(object target, string name, params object?[] args) => target.GetType().GetMethod(name, Members)!.Invoke(target, args);
    private static object? Field(object target, string name) => target.GetType().GetField(name, Members)!.GetValue(target);
    private static void Set(object target, string name, object? value) => target.GetType().GetField(name, Members)!.SetValue(target, value);

    [Fact]
    public async Task Dialog_RejectsRepeatedConfirmationAndCancelWhileActionIsRunning()
    {
        var dialog = new ConfirmDialog();
        var completed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var confirmed = 0; var cancelled = 0;
        typeof(ConfirmDialog).GetProperty("IsOpen")!.SetValue(dialog, true);
        typeof(ConfirmDialog).GetProperty("Confirmed")!.SetValue(dialog, EventCallback.Factory.Create(this, (Func<Task>)(async () => { confirmed++; await completed.Task; })));
        typeof(ConfirmDialog).GetProperty("Cancelled")!.SetValue(dialog, EventCallback.Factory.Create(this, () => cancelled++));
        var first = (Task)Call(dialog, "ConfirmAsync")!;
        await (Task)Call(dialog, "ConfirmAsync")!;
        await (Task)Call(dialog, "CancelAsync")!;
        Assert.Equal(1, confirmed);
        Assert.Equal(0, cancelled);
        completed.SetResult();
        await first;
        await (Task)Call(dialog, "CancelAsync")!;
        Assert.Equal(1, cancelled);
        Assert.Equal(1, confirmed);
    }

    [Fact]
    public void YoloStop_RequestAndCancelLeaveTrainingActive()
    {
        var page = new TrainPage { ProjectId = "project" };
        var status = new TrainingStatus { ProjectId = "project", Phase = TrainingPhase.Training };
        Set(page, "_status", status);
        Set(page, "_projectName", "零件检测");
        // 未注入训练服务：请求和取消阶段不得调用任何停止服务。
        Call(page, "RequestStop");
        Assert.Equal("project", Field(page, "_stopProjectId"));
        Assert.Equal("零件检测", Field(page, "_stopProjectName"));
        Assert.True(status.IsActive);
        Call(page, "CancelStop");
        Assert.Null(Field(page, "_stopProjectId"));
        Assert.True(status.IsActive);
    }

    [Fact]
    public async Task YoloStop_ConfirmedActionCancelsRealTrainingToken()
    {
        var services = new ServiceCollection();
        services.AddLogging(); services.AddSignalR();
        await using var provider = services.BuildServiceProvider();
        await using var training = new TrainingService(provider.GetRequiredService<IHubContext<TrainingHub>>(), provider.GetRequiredService<IServiceScopeFactory>(), NullLogger<TrainingService>.Instance);
        var owner = "confirmtest" + Guid.NewGuid().ToString("N");
        var key = owner + "\nproject";
        var status = new TrainingStatus { Owner = owner, ProjectId = "project", Phase = TrainingPhase.Training };
        ((System.Collections.IDictionary)Field(training, "_statuses")!).Add(key, status);
        using var cancellation = new CancellationTokenSource();
        ((System.Collections.IDictionary)Field(training, "_runCancellations")!).Add(key, cancellation);
        var page = new TrainPage { ProjectId = "project" };
        typeof(TrainPage).GetProperty("Training", Members)!.SetValue(page, training);
        Set(page, "_owner", owner); Set(page, "_status", status.Clone());
        try
        {
            Call(page, "RequestStop");
            Assert.False(cancellation.IsCancellationRequested);
            await (Task)Call(page, "ConfirmStopAsync")!;
            Assert.True(cancellation.IsCancellationRequested);
            Assert.Equal(TrainingPhase.Cancelled, training.GetStatus(owner, "project")!.Phase);
            Assert.Null(Field(page, "_stopProjectId"));
        }
        finally
        {
            var directory = Path.Combine(AppContext.BaseDirectory, "train", "yolo", "statuses", owner);
            if (Directory.Exists(directory)) { Directory.Delete(directory, true); }
        }
    }

    [Fact]
    public async Task AnomalibStop_CancelsOnlyAfterExplicitConfirmation()
    {
        await using var workflow = new AnomalibWorkflowService(null!, null!);
        var shutdown = (CancellationTokenSource)Field(workflow, "_shutdown")!;
        var stateType = typeof(AnomalibWorkflowService).GetNestedType("RunState", BindingFlags.NonPublic)!;
        var state = Activator.CreateInstance(stateType, "alice", "project", new AnomalibTrainingOptions(), shutdown.Token)!;
        var cancellation = (CancellationTokenSource)stateType.GetProperty("Cancellation")!.GetValue(state)!;
        var completion = (TaskCompletionSource)stateType.GetProperty("Completion")!.GetValue(state)!;
        ((System.Collections.IDictionary)Field(workflow, "_runs")!).Add("alice\nproject", state);
        using var registration = cancellation.Token.Register(() => completion.TrySetResult());
        var page = new AnomalibTrainPage { ProjectId = "project" };
        typeof(AnomalibTrainPage).GetProperty("Workflow", Members)!.SetValue(page, workflow);
        Set(page, "_owner", "alice");
        Set(page, "_status", new AnomalibTrainingStatus { Phase = AnomalibTrainingPhase.Training });
        Call(page, "RequestStop");
        Assert.False(cancellation.IsCancellationRequested);
        Call(page, "CancelStop");
        Assert.False(cancellation.IsCancellationRequested);
        Call(page, "RequestStop");
        Call(page, "ConfirmStop");
        Assert.True(cancellation.IsCancellationRequested);
        Assert.Null(Field(page, "_stopProjectId"));
        await workflow.StopAsync(TestContext.Current.CancellationToken);
        cancellation.Dispose();
    }

    [Fact]
    public async Task AnomalibSingleImage_RequestAndCancelKeepImageInProject()
    {
        var page = new AnomalibProjectDetails();
        var task = new AnnotationTask();
        // 不注入 WorkspaceService，确认请求不得提前进入删除持久化流程。
        await (Task)Call(page, "DeleteImageAsync", task)!;
        Assert.Same(task, Assert.Single((AnnotationTask[])Field(page, "_pendingBatch")!));
        Call(page, "CancelBatchDelete");
        Assert.Empty((AnnotationTask[])Field(page, "_pendingBatch")!);
    }
}
