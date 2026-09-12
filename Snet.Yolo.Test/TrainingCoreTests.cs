using Snet.Yolo.Tasks.Core.Training;
using System.Globalization;
using Snet.Yolo.Tasks.Core.Config;
using Xunit;

namespace Snet.Yolo.Test;

[Collection("Database")]
public sealed class TrainingCoreTests
{
    [Theory]
    [InlineData(YoloTaskType.Detect, "yolo26n-seg.pt", "yolo26n.pt")]
    [InlineData(YoloTaskType.Segment, "yolo26n.pt", "yolo26n-seg.pt")]
    [InlineData(YoloTaskType.Segment, "yolo26n-pose.pt", "yolo26n-seg.pt")]
    [InlineData(YoloTaskType.Classify, "yolo11s.pt", "yolo11s-cls.pt")]
    [InlineData(YoloTaskType.Pose, "yolo11m-obb.pt", "yolo11m-pose.pt")]
    [InlineData(YoloTaskType.Obb, "yolo26x-cls.pt", "yolo26x-obb.pt")]
    public void ModelFor_NormalizesModelNameForSelectedTask(YoloTaskType task, string input, string expected)
    {
        Assert.Equal(expected, YoloTaskRegistry.ModelFor(task, input));
    }

    [Theory]
    [InlineData("detect", "detect train")]
    [InlineData("segment", "segment train")]
    [InlineData("classify", "classify train")]
    [InlineData("pose", "pose train")]
    [InlineData("obb", "obb train")]
    public void BuildTrain_UsesExplicitUltralyticsTask(string task, string expectedPrefix)
    {
        var options = new TrainingOptions { Task = task, Model = "yolo26n.pt", Epochs = 10, ImgSize = 640, Device = "cpu" };

        var command = YoloCommandBuilder.BuildTrain("yolo", "data.yaml", options);

        Assert.StartsWith("yolo " + expectedPrefix + " data=data.yaml", command, StringComparison.Ordinal);
        Assert.DoesNotContain(" task=", command, StringComparison.Ordinal);
    }

    [Fact]
    public void ObbTemplate_RoundTripsThroughTaskRegistry()
    {
        var xml = Snet.Yolo.Tasks.Core.Config.YoloTaskRegistry.ConfigFor(Snet.Yolo.Tasks.Core.Config.YoloTaskType.Obb);
        var config = Snet.Yolo.Tasks.Core.Config.LabelingConfigParser.Parse(xml);

        Assert.Equal(Snet.Yolo.Tasks.Core.Config.YoloTaskType.Obb, Snet.Yolo.Tasks.Core.Config.YoloTaskRegistry.FromConfig(config));
    }

    [Fact]
    public void YoloWithImages_IncludesSourceImagesBesideLabels()
    {
        var task = new Snet.Yolo.Tasks.Core.Models.AnnotationTask
        {
            Data = new System.Text.Json.Nodes.JsonObject { ["image"] = "/uploads/project/photo.jpg" },
        };

        var export = Snet.Yolo.Tasks.Core.Serialization.Export.ExportService.YoloWithImages(
            new[] { task }, new Snet.Yolo.Tasks.Core.Config.LabelingConfigModel(), _ => new byte[] { 1, 2, 3 });

        Assert.True(export.IsZip);
        Assert.Contains(export.Files, file => file.Path == "images/1.jpg" && file.Content.SequenceEqual(new byte[] { 1, 2, 3 }));
    }

    /// <summary>确保 COCO Pose 使用固定关键点顺序、缺失点占位，并且不会重复输出父框。</summary>
    [Fact]
    public void CocoPose_EmitsOneObjectWithFixedKeyPointShape()
    {
        const string configXml = """
            <View><Image name="image" value="$image"/><RectangleLabels name="objects" toName="image"><Label value="person"/></RectangleLabels><KeyPointLabels name="points" toName="image"><Label value="nose" model_index="0"/><Label value="eye" model_index="1"/></KeyPointLabels></View>
            """;
        const string taskJson = """
            {"data":{"image":"sample.jpg"},"annotations":[{"result":[{"id":"box","type":"rectanglelabels","original_width":100,"original_height":100,"value":{"x":10,"y":20,"width":30,"height":40,"rectanglelabels":["person"]}},{"id":"eye-row","parentID":"box","type":"keypointlabels","original_width":100,"original_height":100,"value":{"x":25,"y":35,"keypointlabels":["eye"]}}]}]}
            """;
        var config = LabelingConfigParser.Parse(configXml);
        var task = System.Text.Json.JsonSerializer.Deserialize<Snet.Yolo.Tasks.Core.Models.AnnotationTask>(taskJson)!;

        var export = Snet.Yolo.Tasks.Core.Serialization.Export.ExportService.Coco(new[] { task }, config);
        using var document = System.Text.Json.JsonDocument.Parse(Assert.Single(export.Files).Content);
        var annotation = Assert.Single(document.RootElement.GetProperty("annotations").EnumerateArray());
        var keypoints = annotation.GetProperty("keypoints").EnumerateArray().Select(item => item.GetInt32()).ToArray();

        Assert.Equal(new[] { 0, 0, 0, 25, 35, 2 }, keypoints);
        Assert.Equal(1, annotation.GetProperty("num_keypoints").GetInt32());
        var category = Assert.Single(document.RootElement.GetProperty("categories").EnumerateArray());
        Assert.Equal(new[] { "nose", "eye" }, category.GetProperty("keypoints").EnumerateArray().Select(item => item.GetString()));
    }

    [Fact]
    public async Task ProjectAggregateWrites_AreTransactionalAndQueryable()
    {
        var projectKey = "test-" + Guid.NewGuid().ToString("N");
        var owner = "owner-" + Guid.NewGuid().ToString("N");
        await using var projects = new Snet.Yolo.Server.ProjectOperate(projectKey + "-projects");
        await using var tasks = new Snet.Yolo.Server.ProjectTaskOperate(projectKey + "-tasks");
        var project = new Snet.Yolo.Server.models.data.ProjectData { owner = owner, projectId = projectKey, name = "transaction-test" };

        var added = await projects.AddAsync(project);
        Assert.True(added.Status, added.Message);
        try
        {
            var queriedProject = await projects.QueryAsync(owner, projectKey);
            Assert.True(queriedProject.GetDetails(out List<Snet.Yolo.Server.models.data.ProjectData>? projectRows));
            var storageId = Assert.Single(projectRows!).id;

            var replaced = await tasks.ReplaceTasksAsync(storageId, new List<Snet.Yolo.Server.models.data.TaskData>
            {
                new() { projectId = storageId, taskIndex = 0, dataJson = "{\"value\":1}" },
            });
            Assert.True(replaced.Status, replaced.Message);
            var queriedTasks = await tasks.QueryTasksAsync(storageId);
            Assert.True(queriedTasks.GetDetails(out List<Snet.Yolo.Server.models.data.TaskData>? taskRows));
            Assert.Single(taskRows!);

            var deleted = await projects.DeleteAggregateAsync(storageId, owner, projectKey);
            Assert.True(deleted.Status, deleted.Message);
            var afterDelete = await tasks.QueryTasksAsync(storageId);
            var hasRemaining = afterDelete.GetDetails(out List<Snet.Yolo.Server.models.data.TaskData>? remaining);
            Assert.True(!hasRemaining || remaining is null || remaining.Count == 0);
        }
        finally
        {
            await projects.DeleteAsync(owner, projectKey);
        }
    }

    [Fact]
    public async Task UserUpdate_PersistsSelectedFieldsAndNewPassword()
    {
        var key = "test-user-" + Guid.NewGuid().ToString("N");
        await using var users = new Snet.Yolo.Server.UserOperate(key);
        var username = "user-" + Guid.NewGuid().ToString("N");
        var index = 0;

        var added = await users.AddAsync(username, "old-password", "User");
        Assert.True(added.Status, added.Message);
        try
        {
            var query = await users.QueryAsync();
            Assert.True(query.GetDetails(out List<Snet.Yolo.Server.models.data.UserData>? rows));
            index = Assert.Single(rows!, user => user.username == username).index;

            var updated = await users.UpdateAsync(index, "new-password", "Admin", false);
            Assert.True(updated.Status, updated.Message);
            var afterUpdate = await users.QueryAsync(index);
            Assert.True(afterUpdate.GetDetails(out List<Snet.Yolo.Server.models.data.UserData>? updatedRows));
            var updatedUser = Assert.Single(updatedRows!);
            Assert.Equal("Admin", updatedUser.role);
            Assert.Equal(0, updatedUser.active);
            Assert.False((await users.VerifyAsync(username, "new-password")).Status);

            var reactivated = await users.UpdateAsync(index, null, null, true);
            Assert.True(reactivated.Status, reactivated.Message);
            Assert.True((await users.VerifyAsync(username, "new-password")).Status);
        }
        finally
        {
            if (index != 0) { await users.DeleteAsync(index); }
        }
    }

    [Fact]
    public async Task BootstrapPassword_CanRecoverExistingAdministrator()
    {
        var previous = Environment.GetEnvironmentVariable("SNET_BOOTSTRAP_ADMIN_PASSWORD");
        var password = "recovery-" + Guid.NewGuid().ToString("N");
        try
        {
            await using (var seed = new Snet.Yolo.Server.UserOperate("seed-" + Guid.NewGuid().ToString("N")))
            {
                Assert.True((await seed.QueryAsync()).Status);
            }

            Environment.SetEnvironmentVariable("SNET_BOOTSTRAP_ADMIN_PASSWORD", password);
            await using var recovered = new Snet.Yolo.Server.UserOperate("recover-" + Guid.NewGuid().ToString("N"));
            Assert.True((await recovered.QueryAsync()).Status);
            Assert.True((await recovered.VerifyAsync("snet", password)).Status);
        }
        finally
        {
            Environment.SetEnvironmentVariable("SNET_BOOTSTRAP_ADMIN_PASSWORD", previous);
        }
    }

    [Fact]
    public async Task EmptyUserStore_CreatesRequestedDefaultAdministrator()
    {
        var previous = Environment.GetEnvironmentVariable("SNET_BOOTSTRAP_ADMIN_PASSWORD");
        try
        {
            Environment.SetEnvironmentVariable("SNET_BOOTSTRAP_ADMIN_PASSWORD", null);
            var databasePath = Path.Combine(
                Snet.Yolo.Server.handler.PublicHandler.DefaultPath,
                "db",
                Snet.Yolo.Server.handler.PublicHandler.DefaultDBName);
            await using (var connection = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={databasePath}"))
            {
                await connection.OpenAsync();
                await using var command = connection.CreateCommand();
                command.CommandText = "DELETE FROM [UserData]";
                await command.ExecuteNonQueryAsync();
            }

            await using var initialized = new Snet.Yolo.Server.UserOperate("initialize-" + Guid.NewGuid().ToString("N"));
            Assert.True((await initialized.QueryAsync()).Status);
            Assert.True((await initialized.VerifyAsync("snet", "123456")).Status);
        }
        finally
        {
            Environment.SetEnvironmentVariable("SNET_BOOTSTRAP_ADMIN_PASSWORD", previous);
        }
    }

    [Fact]
    public void ParseProgress_IsCultureIndependentAndStripsAnsi()
    {
        var previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("fr-FR");
            var update = YoloOutputParser.Parse("\u001b[2K  3/10  1.2G  0.125  0.250  0.375  2  640: 100%");

            Assert.NotNull(update);
            Assert.Equal(3, update.Epoch);
            Assert.Equal(10, update.TotalEpochs);
            Assert.Equal(30, update.Percent);
            Assert.Equal(0.125, update.BoxLoss);
            Assert.Equal(0.250, update.ClsLoss);
            Assert.Equal(0.375, update.DflLoss);
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }

    [Fact]
    public void ParseMetrics_ReturnsAllValidationMetrics()
    {
        var update = YoloOutputParser.ParseMetrics("all  12  18  0.91  0.82  0.73  0.64");

        Assert.NotNull(update);
        Assert.Equal(0.91, update.Precision);
        Assert.Equal(0.82, update.Recall);
        Assert.Equal(0.73, update.Map50);
        Assert.Equal(0.64, update.Map5095);
    }

    [Fact]
    public void DataYaml_EscapesNamesAndUsesAvailableValidationFolder()
    {
        var yaml = DataYamlBuilder.Build(new[] { "cat", "a\"b" }, @"C:\dataset", useVal: true, hasValDir: true, keypointCount: 4);

        Assert.Contains("path: C:/dataset", yaml);
        Assert.Contains("val: val/images", yaml);
        Assert.Contains("names: [\"cat\", \"a\\\"b\"]", yaml);
        Assert.Contains("kpt_shape: [4, 3]", yaml);
    }

    [Fact]
    public void StatusClone_CanExcludeLargeLogTailAndRemainsIndependent()
    {
        var source = new TrainingStatus { ProjectId = "p", LogTail = new List<string> { "one" } };

        var clone = source.Clone(includeLogs: false);
        clone.Metrics.Map50 = 0.5;

        Assert.Empty(clone.LogTail);
        Assert.Null(source.Metrics.Map50);
    }
}
