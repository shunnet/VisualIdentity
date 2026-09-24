using Microsoft.Data.Sqlite;
using Snet.Yolo.Server;
using Snet.Yolo.Server.handler;
using Snet.Yolo.Server.models.data;
using Snet.Yolo.Server.models.@enum;
using Snet.Yolo.Server.models;
using Snet.Yolo.Tasks.Services;
using Xunit;

namespace Snet.Yolo.Test;

/// <summary>验证 Anomalib 工程类型的持久化、兼容迁移和租户隔离。</summary>
[Collection("Database")]
public sealed class AnomalibProjectTests
{
    /// <summary>未显式指定类型的旧式工程必须保持为 YOLO 工程。</summary>
    [Fact]
    public async Task AddAsync_DefaultsLegacyProjectToYolo()
    {
        await using var projects = new ProjectOperate("anomalib-legacy-" + Guid.NewGuid().ToString("N"));
        var owner = "owner-" + Guid.NewGuid().ToString("N");
        var projectId = "project-" + Guid.NewGuid().ToString("N");
        try
        {
            Assert.True((await projects.AddAsync(new ProjectData { owner = owner, projectId = projectId, name = "legacy" })).Status);

            var query = await projects.QueryAsync(owner, projectId);

            Assert.True(query.GetDetails(out List<ProjectData>? rows));
            Assert.Equal(ProjectKind.Yolo, Assert.Single(rows!).kind);
        }
        finally
        {
            await projects.DeleteAsync(owner, projectId);
        }
    }

    /// <summary>按类型查询时只返回当前租户且类型匹配的工程。</summary>
    [Fact]
    public async Task QueryByOwnerAsync_FiltersByOwnerAndProjectKind()
    {
        await using var projects = new ProjectOperate("anomalib-filter-" + Guid.NewGuid().ToString("N"));
        var owner = "owner-" + Guid.NewGuid().ToString("N");
        var otherOwner = "other-" + Guid.NewGuid().ToString("N");
        var yoloId = "yolo-" + Guid.NewGuid().ToString("N");
        var anomalibId = "anomalib-" + Guid.NewGuid().ToString("N");
        var foreignId = "foreign-" + Guid.NewGuid().ToString("N");
        try
        {
            Assert.True((await projects.AddAsync(new ProjectData { owner = owner, projectId = yoloId, name = "yolo", kind = ProjectKind.Yolo })).Status);
            Assert.True((await projects.AddAsync(new ProjectData { owner = owner, projectId = anomalibId, name = "anomalib", kind = ProjectKind.Anomalib })).Status);
            Assert.True((await projects.AddAsync(new ProjectData { owner = otherOwner, projectId = foreignId, name = "foreign", kind = ProjectKind.Anomalib })).Status);

            var query = await projects.QueryByOwnerAsync(owner, ProjectKind.Anomalib);

            Assert.True(query.GetDetails(out List<ProjectData>? rows));
            var row = Assert.Single(rows!);
            Assert.Equal(anomalibId, row.projectId);
            Assert.Equal(owner, row.owner);
            Assert.Equal(ProjectKind.Anomalib, row.kind);
        }
        finally
        {
            await projects.DeleteAsync(owner, yoloId);
            await projects.DeleteAsync(owner, anomalibId);
            await projects.DeleteAsync(otherOwner, foreignId);
        }
    }

    /// <summary>初始化已有数据库时必须创建带 YOLO 默认值的工程类型字段。</summary>
    [Fact]
    public async Task InitializeAsync_EnsuresProjectKindColumnWithYoloDefault()
    {
        await using var projects = new ProjectOperate("anomalib-schema-" + Guid.NewGuid().ToString("N"));

        Assert.True((await projects.InitializeAsync()).Status);

        var databasePath = Path.Combine(PublicHandler.DefaultPath, "db", PublicHandler.DefaultDBName);
        await using var connection = new SqliteConnection($"Data Source={databasePath}");
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA table_info([project])";
        await using var reader = await command.ExecuteReaderAsync();
        var found = false;
        while (await reader.ReadAsync())
        {
            if (!string.Equals(reader.GetString(1), "kind", StringComparison.OrdinalIgnoreCase)) { continue; }
            found = true;
            Assert.Equal("0", reader.GetString(4).Trim('(', ')', '\'', '"'));
            break;
        }
        Assert.True(found);
    }

    /// <summary>Anomalib 图片入口必须携带专用上传类型和目标工程标识。</summary>
    [Fact]
    public void AnomalibProjectIntent_CreatesTypeIsolatedUploadIntent()
    {
        var intent = UploadCenter.AnomalibProjectIntent("scope", "project");

        Assert.Equal(UploadKind.AnomalibProjectImages, intent.Kind);
        Assert.Equal("scope", intent.ScopeKey);
        Assert.Equal("project", intent.ProjectId);
    }
}
