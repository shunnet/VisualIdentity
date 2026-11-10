using Snet.Yolo.Server.models;
using Snet.Yolo.Tasks.Core.History;
using Xunit;

namespace Snet.Yolo.Test;

/// <summary>训练记录文件存储：保存、列出、查找、删除，以及路径安全。</summary>
public sealed class TrainingHistoryStoreTests : IDisposable
{
    /// <summary>每个测试独立的训练根目录。</summary>
    private readonly string _root = Path.Combine(Path.GetTempPath(), "history-" + Guid.NewGuid().ToString("N"));

    /// <summary>删除临时目录。</summary>
    public void Dispose()
    {
        if (Directory.Exists(_root)) { Directory.Delete(_root, recursive: true); }
    }

    /// <summary>构造一条记录。</summary>
    private static TrainingRunRecord Record(string id, DateTime finished, ProjectKind kind = ProjectKind.Yolo)
        => new()
        {
            Id = id, ProjectId = "project1", Kind = kind, Status = TrainingRunStatus.Complete,
            StartedAt = finished.AddMinutes(-5), FinishedAt = finished, Algorithm = "yolo26n.pt", ImageSize = 640,
            Yolo = new YoloRunDetails { Map5095 = 0.5, Curve = [new TrainingCurvePoint { Epoch = 1, Score = 0.1, Loss = 2 }] },
        };

    /// <summary>保存后能按标识读回，字段完整。</summary>
    [Fact]
    public async Task SaveAndFind_RoundTripsRecord()
    {
        var store = new TrainingHistoryStore(_root);
        var finished = new DateTime(2026, 11, 10, 8, 0, 0, DateTimeKind.Utc);
        await store.SaveAsync("snet", Record("run-1", finished), TestContext.Current.CancellationToken);

        var loaded = await store.FindAsync(ProjectKind.Yolo, "snet", "project1", "run-1", TestContext.Current.CancellationToken);

        Assert.NotNull(loaded);
        Assert.Equal("yolo26n.pt", loaded.Algorithm);
        Assert.Equal(0.5, loaded.Yolo?.Map5095);
        Assert.Equal(TrainingRunStatus.Complete, loaded.Status);
        Assert.Single(loaded.Yolo!.Curve);
        Assert.Equal(300, loaded.DurationSeconds);
    }

    /// <summary>列表按结束时间从新到旧排列，并忽略损坏的文件和其他算法族的记录。</summary>
    [Fact]
    public async Task List_SortsNewestFirstAndSkipsCorruptFiles()
    {
        var store = new TrainingHistoryStore(_root);
        var baseTime = new DateTime(2026, 11, 10, 8, 0, 0, DateTimeKind.Utc);
        await store.SaveAsync("snet", Record("old", baseTime), TestContext.Current.CancellationToken);
        await store.SaveAsync("snet", Record("new", baseTime.AddHours(1)), TestContext.Current.CancellationToken);
        var directory = Path.Combine(store.ProjectDirectory(ProjectKind.Yolo, "snet", "project1"), "history");
        await File.WriteAllTextAsync(Path.Combine(directory, "broken.json"), "{ not json", TestContext.Current.CancellationToken);

        var list = await store.ListAsync(ProjectKind.Yolo, "snet", "project1", TestContext.Current.CancellationToken);

        Assert.Equal(["new", "old"], list.Select(run => run.Id));
        Assert.Empty(await store.ListAsync(ProjectKind.Anomalib, "snet", "project1", TestContext.Current.CancellationToken));
    }

    /// <summary>不同用户的记录互相隔离。</summary>
    [Fact]
    public async Task List_IsolatesOwners()
    {
        var store = new TrainingHistoryStore(_root);
        await store.SaveAsync("alice", Record("run-a", DateTime.UtcNow), TestContext.Current.CancellationToken);
        Assert.Empty(await store.ListAsync(ProjectKind.Yolo, "bob", "project1", TestContext.Current.CancellationToken));
        Assert.Single(await store.ListAsync(ProjectKind.Yolo, "alice", "project1", TestContext.Current.CancellationToken));
    }

    /// <summary>删除记录文件后查找不到；不存在或标识含路径分隔符时返回 false。</summary>
    [Fact]
    public async Task DeleteRecord_RemovesFileAndRejectsUnsafeIds()
    {
        var store = new TrainingHistoryStore(_root);
        await store.SaveAsync("snet", Record("run-1", DateTime.UtcNow), TestContext.Current.CancellationToken);

        Assert.True(store.DeleteRecord(ProjectKind.Yolo, "snet", "project1", "run-1"));
        Assert.False(store.DeleteRecord(ProjectKind.Yolo, "snet", "project1", "run-1"));
        Assert.False(store.DeleteRecord(ProjectKind.Yolo, "snet", "project1", "../run-1"));
        Assert.Null(await store.FindAsync(ProjectKind.Yolo, "snet", "project1", "run-1", TestContext.Current.CancellationToken));
    }

    /// <summary>记录中的相对路径不能越出工程训练目录。</summary>
    [Fact]
    public void ResolveInProject_RejectsPathTraversal()
    {
        var store = new TrainingHistoryStore(_root);
        var inside = store.ResolveInProject(ProjectKind.Yolo, "snet", "project1", "runs/train/weights/best.pt");
        Assert.StartsWith(store.ProjectDirectory(ProjectKind.Yolo, "snet", "project1"), inside);
        Assert.Throws<InvalidDataException>(() => store.ResolveInProject(ProjectKind.Yolo, "snet", "project1", "../project2/best.pt"));
        Assert.Throws<ArgumentException>(() => store.ProjectDirectory(ProjectKind.Yolo, "snet", "../other"));
    }
}
