using Microsoft.Extensions.Logging.Abstractions;
using Snet.Yolo.Server.anomalib;
using Snet.Yolo.Server.models;
using Snet.Yolo.Tasks.Core.History;
using Snet.Yolo.Tasks.Services;
using Xunit;

namespace Snet.Yolo.Test;

/// <summary>训练指标页的后端：删除与导出只能触及该工程自己的训练产物，磁盘上的记录不被信任。</summary>
public sealed class TrainingHistoryServiceTests : IDisposable
{
    /// <summary>测试用户。</summary>
    private const string Owner = "snet";

    /// <summary>测试工程。</summary>
    private const string Project = "p1";

    /// <summary>每个测试独立的训练根目录。</summary>
    private readonly string _root = Path.Combine(Path.GetTempPath(), "history-service-" + Guid.NewGuid().ToString("N"));

    /// <summary>训练记录存储。</summary>
    private readonly TrainingHistoryStore _store;

    /// <summary>被测服务；YOLO 训练服务只用于类型依赖，这些测试不会启动训练。</summary>
    private readonly TrainingHistoryService _service;

    /// <summary>创建存储与服务。</summary>
    public TrainingHistoryServiceTests()
    {
        _store = new TrainingHistoryStore(_root);
        var yolo = new TrainingService(null!, null!, NullLogger<TrainingService>.Instance, null, _store);
        _service = new TrainingHistoryService(_store, yolo, new AnomalibModelRegistry(Path.Combine(_root, "anomalib")));
    }

    /// <summary>删除临时目录。</summary>
    public void Dispose() => Directory.Delete(_root, recursive: true);

    /// <summary>创建一个文件（连同目录）并返回路径。</summary>
    private static string CreateFile(string path, byte[]? content = null)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, content ?? [1, 2, 3]);
        return path;
    }

    /// <summary>YOLO 工程目录。</summary>
    private string YoloProject => _store.ProjectDirectory(ProjectKind.Yolo, Owner, Project);

    /// <summary>构造一条记录。</summary>
    private static TrainingRunRecord Record(ProjectKind kind, string id, string runDirectory, string modelPath = "")
        => new()
        {
            Id = id, ProjectId = Project, Kind = kind, Status = TrainingRunStatus.Complete, Algorithm = "yolo26n.pt",
            RunDirectory = runDirectory, ModelPath = modelPath, ModelBytes = modelPath.Length == 0 ? 0 : 3,
        };

    /// <summary>记录里的输出目录写成 "runs/"（解析后就是 runs 目录本身）时必须拒绝，其他训练不受影响。</summary>
    [Fact]
    public async Task Delete_RecordPointingAtTheRunsRoot_IsRefusedAndKeepsEveryRun()
    {
        var other = CreateFile(Path.Combine(YoloProject, "runs", "other", "weights", "best.pt"));
        await _store.SaveAsync(Owner, Record(ProjectKind.Yolo, "tampered", "runs/"));

        var result = await _service.DeleteAsync(ProjectKind.Yolo, Owner, Project, "tampered");

        Assert.Equal(TrainingDeleteResult.Failed, result);
        Assert.True(File.Exists(other));
        Assert.NotNull(await _store.FindAsync(ProjectKind.Yolo, Owner, Project, "tampered"));
    }

    /// <summary>正常的 YOLO 训练只删除它自己的输出目录与记录。</summary>
    [Fact]
    public async Task Delete_ValidYoloRun_RemovesOnlyThatRunAndItsRecord()
    {
        var kept = CreateFile(Path.Combine(YoloProject, "runs", "b", "weights", "best.pt"));
        CreateFile(Path.Combine(YoloProject, "runs", "a", "weights", "best.pt"));
        await _store.SaveAsync(Owner, Record(ProjectKind.Yolo, "a", "runs/a", "runs/a/weights/best.pt"));

        var result = await _service.DeleteAsync(ProjectKind.Yolo, Owner, Project, "a");

        Assert.Equal(TrainingDeleteResult.Deleted, result);
        Assert.False(Directory.Exists(Path.Combine(YoloProject, "runs", "a")));
        Assert.True(File.Exists(kept));
        Assert.Null(await _store.FindAsync(ProjectKind.Yolo, Owner, Project, "a"));
    }

    /// <summary>Anomalib 的运行目录必须是工程目录的直接子目录；嵌套路径被拒绝，合法目录被删除。</summary>
    [Fact]
    public async Task Delete_AnomalibRunDirectory_MustBeADirectChildOfTheProject()
    {
        var anomalibProject = _store.ProjectDirectory(ProjectKind.Anomalib, Owner, Project);
        var nested = CreateFile(Path.Combine(anomalibProject, "nested", "anomalib-run-x", "artifacts", "model.onnx"));
        CreateFile(Path.Combine(anomalibProject, "anomalib-run-ok", "artifacts", "model.onnx"));
        await _store.SaveAsync(Owner, Record(ProjectKind.Anomalib, "nested-run", "nested/anomalib-run-x"));
        await _store.SaveAsync(Owner, Record(ProjectKind.Anomalib, "ok-run", "anomalib-run-ok"));

        Assert.Equal(TrainingDeleteResult.Failed, await _service.DeleteAsync(ProjectKind.Anomalib, Owner, Project, "nested-run"));
        Assert.True(File.Exists(nested));
        Assert.Equal(TrainingDeleteResult.Deleted, await _service.DeleteAsync(ProjectKind.Anomalib, Owner, Project, "ok-run"));
        Assert.False(Directory.Exists(Path.Combine(anomalibProject, "anomalib-run-ok")));
    }

    /// <summary>YOLO 的 .pt 下载只接受 runs 目录之下的 .pt 文件；工程里的其他文件即使路径合法也不能被当成权重下载。</summary>
    [Fact]
    public async Task Download_YoloModel_OnlyServesPtFilesUnderRuns()
    {
        CreateFile(Path.Combine(YoloProject, "history", "secret.json"), [9, 9]);
        CreateFile(Path.Combine(YoloProject, "runs", "a", "weights", "notes.txt"), [9, 9]);
        CreateFile(Path.Combine(YoloProject, "runs", "a", "weights", "best.pt"), [1, 2, 3]);
        await _store.SaveAsync(Owner, Record(ProjectKind.Yolo, "outside", "runs/a", "history/secret.json"));
        await _store.SaveAsync(Owner, Record(ProjectKind.Yolo, "text", "runs/a", "runs/a/weights/notes.txt"));
        await _store.SaveAsync(Owner, Record(ProjectKind.Yolo, "good", "runs/a", "runs/a/weights/best.pt"));

        Assert.Null(await _service.OpenDownloadAsync(ProjectKind.Yolo, Owner, Project, "outside", "pt"));
        Assert.Null(await _service.OpenDownloadAsync(ProjectKind.Yolo, Owner, Project, "text", "pt"));
        var download = await _service.OpenDownloadAsync(ProjectKind.Yolo, Owner, Project, "good", "pt");

        Assert.NotNull(download);
        await using var stream = download.Stream;
        Assert.Equal(3, stream.Length);
        Assert.EndsWith(".pt", download.FileName, StringComparison.Ordinal);
    }
}
