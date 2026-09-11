using Snet.Yolo.Server;
using Snet.Yolo.Server.models.data;
using Snet.Yolo.Server.models.@enum;
using Xunit;

namespace Snet.Yolo.Test;

/// <summary>验证持久层查询必须按用户归属隔离。</summary>
[Collection("Database")]
public sealed class DataIsolationTests
{
    /// <summary>即使两个用户使用相同工程标识，也只能查询到自己的工程。</summary>
    [Fact]
    public async Task Projects_AreFilteredByOwner()
    {
        var projects = new ProjectOperate("isolation-test-" + Guid.NewGuid().ToString("N"));
        var projectId = "isolation-" + Guid.NewGuid().ToString("N");
        var firstOwner = "first-" + Guid.NewGuid().ToString("N");
        var secondOwner = "second-" + Guid.NewGuid().ToString("N");
        try
        {
            Assert.True((await projects.AddAsync(new ProjectData { owner = firstOwner, projectId = projectId, name = "first" })).Status);
            Assert.True((await projects.AddAsync(new ProjectData { owner = secondOwner, projectId = projectId, name = "second" })).Status);

            var firstResult = await projects.QueryAsync(firstOwner, projectId);
            var secondResult = await projects.QueryAsync(secondOwner, projectId);
            Assert.True(firstResult.GetDetails(out List<ProjectData>? first));
            Assert.True(secondResult.GetDetails(out List<ProjectData>? second));
            Assert.Equal("first", Assert.Single(first!).name);
            Assert.Equal("second", Assert.Single(second!).name);
        }
        finally
        {
            await projects.DeleteAsync(projectId);
        }
    }

    /// <summary>模型列表和单模型查询不会返回其他用户的模型。</summary>
    [Fact]
    public async Task Models_AreFilteredByOwner()
    {
        var models = new ManageOperate("model-isolation-test-" + Guid.NewGuid().ToString("N"));
        var owner = "owner-" + Guid.NewGuid().ToString("N");
        var otherOwner = "other-" + Guid.NewGuid().ToString("N");
        var file = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".onnx");
        await File.WriteAllBytesAsync(file, [1, 2, 3, 4]);
        var index = 0;
        try
        {
            Assert.True((await models.AddAsync(owner, file, "isolated", OnnxType.ObjectDetection)).Status);
            var ownResult = await models.QueryByOwnerAsync(owner);
            Assert.True(ownResult.GetDetails(out List<OnnxData>? ownModels));
            index = Assert.Single(ownModels!, model => model.name == Path.GetFileName(file)).index;

            var otherResult = await models.QueryAsync(otherOwner, index);
            Assert.False(otherResult.GetDetails(out List<OnnxData>? otherModels) && otherModels is { Count: > 0 });
        }
        finally
        {
            if (index > 0) { await models.DeleteAsync(owner, index, false); }
            File.Delete(file);
        }
    }
}
