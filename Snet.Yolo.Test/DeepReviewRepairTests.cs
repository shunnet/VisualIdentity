using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.Extensions.Logging.Abstractions;
using SkiaSharp;
using Snet.Yolo.Server;
using Snet.Yolo.Server.handler;
using Snet.Yolo.Server.models.data;
using Snet.Yolo.Tasks.Core.Config;
using Snet.Yolo.Tasks.Core.Editing;
using Snet.Yolo.Tasks.Core.Models;
using Snet.Yolo.Tasks.Core.Serialization.Export;
using Snet.Yolo.Tasks.Core.Workspace;
using Snet.Yolo.Tasks.Services;
using System.Reflection;
using System.Security.Claims;
using System.Text.Json.Nodes;
using Xunit;

namespace Snet.Yolo.Test;

/// <summary>全仓审查修复的行为回归；不下载模型，不依赖 GPU。</summary>
public sealed class DeepReviewRepairTests
{
    private sealed class TestUser(string owner) : AuthenticationStateProvider
    {
        public override Task<AuthenticationState> GetAuthenticationStateAsync()
            => Task.FromResult(new AuthenticationState(new ClaimsPrincipal(new ClaimsIdentity([new(ClaimTypes.Name, owner)], "test"))));
    }

    [Fact]
    public async Task Workspace_StaleSaveRejected_ImportPreservesLatestAnnotations_DeletedProjectNotRecreated()
    {
        var owner = "review-" + Guid.NewGuid().ToString("N");
        var project = new WorkspaceProject { Name = "并发保存回归", Tasks = [new() { Id = 1, Data = new JsonObject { ["image"] = "original.png" } }] };
        await using var projects = new ProjectOperate(owner + "-projects");
        await using var tasks = new ProjectTaskOperate(owner + "-tasks");
        // 这些持久化操作不访问训练服务，也不启动任何训练进程。
        var service = new WorkspaceService(projects, tasks, null!, new CurrentUserContext(new TestUser(owner)), NullLogger<WorkspaceService>.Instance);
        await service.SaveProjectAsync(project);
        var stale = (await service.GetProjectAsync(project.Id))!;
        try
        {
            var latest = (await service.GetProjectAsync(project.Id))!;
            latest.Tasks[0].Data!["confirmed"] = "已保存的新标注";
            await service.SaveTaskSnapshotAsync(project.Id, 0, 1, service.CreateTaskSnapshot(latest.Tasks[0]));
            await Assert.ThrowsAsync<InvalidOperationException>(() => service.SaveProjectAsync(stale));
            await service.AppendTasksAsync(stale, [new() { Data = new JsonObject { ["image"] = "new.png" } }]);
            var loaded = (await service.GetProjectAsync(project.Id))!;
            Assert.Equal(2, loaded.Tasks.Count);
            Assert.Equal("已保存的新标注", loaded.Tasks[0].Data!["confirmed"]!.ToString());
            Assert.Equal(2, loaded.Tasks[1].Id);
            var query = await projects.QueryAsync(owner, project.Id);
            Assert.True(query.GetDetails(out List<ProjectData>? rows));
            Assert.True((await projects.DeleteAggregateAsync(Assert.Single(rows!).id, owner, project.Id)).Status);
            await Assert.ThrowsAsync<InvalidOperationException>(() => service.SaveProjectAsync(loaded));
            Assert.Null(await service.GetProjectAsync(project.Id));
        }
        finally
        {
            var query = await projects.QueryAsync(owner, project.Id);
            if (query.GetDetails(out List<ProjectData>? rows) && rows is { Count: > 0 })
            { await projects.DeleteAggregateAsync(rows[0].id, owner, project.Id); }
        }
    }

    [Fact]
    public void Obb_ExportMatchesPixelCenterRotation_OnNonSquareImage()
    {
        var task = TaskWith(new ResultRow { Type = RegionType.RectangleLabels, OriginalWidth = 1000, OriginalHeight = 500,
            Value = new JsonObject { ["x"] = 20, ["y"] = 20, ["width"] = 20, ["height"] = 20, ["rotation"] = 90, ["rectanglelabels"] = new JsonArray("box") } });
        var actual = YoloLabelExporter.Build(task, YoloTaskType.Obb, ["box"]).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)
            .Skip(1).Select(value => double.Parse(value, System.Globalization.CultureInfo.InvariantCulture)).ToArray();
        double[] expected = [.35, .1, .35, .5, .25, .5, .25, .1];
        for (var i = 0; i < expected.Length; i++) { Assert.Equal(expected[i], actual[i], 6); }
    }

    [Fact]
    public void LabelRename_IsScopedToControl_AndPolygonExportIsNotEmpty()
    {
        const string xml = "<View><Image name='image' value='$image'/><PolygonLabels name='poly' toName='image'><Label value='object' background='#ff0000'/></PolygonLabels><BrushLabels name='brush' toName='image'><Label value='object' background='#0000ff'/></BrushLabels></View>";
        var before = LabelingConfigParser.Parse(xml);
        var after = LabelingConfigParser.Parse(xml.Replace("value='object' background='#ff0000'", "value='defect' background='#ff0000'"));
        var polygon = new ResultRow { Type = RegionType.PolygonLabels, FromName = "poly", Value = new JsonObject {
            ["polygonlabels"] = new JsonArray("object"), ["points"] = new JsonArray(new JsonArray(10, 20), new JsonArray(40, 20), new JsonArray(40, 60)) } };
        var task = TaskWith(polygon);
        Assert.Equal(1, LabelSync.Apply([task], before, after).RenamedRegions);
        Assert.Equal("defect", polygon.Value!["polygonlabels"]![0]!.ToString());
        var exported = ExportService.YoloWithImages([task], after, _ => [1]);
        Assert.NotEmpty(Assert.Single(exported.Files, file => file.Path == "labels/1.txt").Content);
    }

    [Fact]
    public void Classification_ExportUsesClassFolders()
    {
        var config = LabelingConfigParser.Parse("<View><Image name='image' value='$image'/><Choices name='choice' toName='image'><Choice value='good'/></Choices></View>");
        var task = TaskWith(new ResultRow { Type = RegionType.Choices, Value = new JsonObject { ["choices"] = new JsonArray("good") } });
        var result = ExportService.YoloWithImages([task], config, _ => [1]);
        Assert.Contains(result.Files, file => file.Path == "train/good/1.png");
    }

    [Theory]
    [InlineData(1, 120, 80, "red")]
    [InlineData(2, 120, 80, "green")]
    [InlineData(3, 120, 80, "yellow")]
    [InlineData(4, 120, 80, "blue")]
    [InlineData(5, 80, 120, "red")]
    [InlineData(6, 80, 120, "blue")]
    [InlineData(7, 80, 120, "yellow")]
    [InlineData(8, 80, 120, "green")]
    public void ImageDecoder_NormalizesExifOrientation(int orientation, int width, int height, string expectedCorner)
    {
        using var source = new SKBitmap(120, 80);
        for (var y = 0; y < 80; y++) for (var x = 0; x < 120; x++)
        { source.SetPixel(x, y, y < 40 ? (x < 60 ? SKColors.Red : SKColors.Lime) : (x < 60 ? SKColors.Blue : SKColors.Yellow)); }
        using var encoded = source.Encode(SKEncodedImageFormat.Jpeg, 90);
        byte[] exif = [69,120,105,102,0,0,73,73,42,0,8,0,0,0,1,0,18,1,3,0,1,0,0,0,(byte)orientation,0,0,0,0,0,0,0];
        var jpeg = encoded.ToArray();
        using var stream = new MemoryStream();
        stream.Write(jpeg.AsSpan(0, 2)); stream.Write([255,225,0,34]); stream.Write(exif); stream.Write(jpeg.AsSpan(2));
        using var decoded = ImageDecoder.Decode(stream.ToArray());
        Assert.Equal(width, decoded.Width); Assert.Equal(height, decoded.Height);
        var corner = decoded.GetPixel(width / 4, height / 4);
        var expected = expectedCorner switch { "red" => SKColors.Red, "green" => SKColors.Lime, "blue" => SKColors.Blue, _ => SKColors.Yellow };
        Assert.InRange(Math.Abs(corner.Red - expected.Red), 0, 20);
        Assert.InRange(Math.Abs(corner.Green - expected.Green), 0, 20);
        Assert.InRange(Math.Abs(corner.Blue - expected.Blue), 0, 20);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void PoseLayout_UsesActualKeypointCount_ToDisambiguateMultiClassOutput(bool endToEnd)
    {
        var type = typeof(YoloDotNet.Yolo).Assembly.GetType("YoloDotNet.Modules.V26.Yolo26OutputLayoutResolver")!;
        var resolve = type.GetMethod("ResolveVariable", BindingFlags.Static | BindingFlags.NonPublic)!;
        var shape = endToEnd ? new[] { 1, 300, 57 } : new[] { 1, 57, 8400 };
        var layout = resolve.Invoke(null, [shape, 6, 6, 3, "pose", 17]);
        Assert.Equal(endToEnd ? "EndToEnd" : "Raw", layout!.ToString());
    }

    private static AnnotationTask TaskWith(ResultRow row)
        => new() { Data = new JsonObject { ["image"] = "test.png" }, Annotations = [new() { Result = [row] }] };

    private delegate YoloDotNet.Models.ObjectResult[] Suppress(Span<YoloDotNet.Models.ObjectResult> boxes, double threshold);

    [Theory]
    [InlineData(true, 1)]
    [InlineData(false, 2)]
    public void Nms_OnlySuppressesOverlapsWithinSameClass(bool sameClass, int expected)
    {
        var type = typeof(YoloDotNet.Yolo).Assembly.GetType("YoloDotNet.Core.YoloCore")!;
        var instance = System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(type);
        var suppress = type.GetMethod("RemoveOverlappingBoxes")!.CreateDelegate<Suppress>(instance);
        YoloDotNet.Models.ObjectResult[] boxes = [
            new() { Label = new() { Index = 0 }, Confidence = .95, BoundingBox = new(0, 0, 100, 100) },
            new() { Label = new() { Index = sameClass ? 0 : 1 }, Confidence = .9, BoundingBox = new(0, 0, 100, 100) }
        ];
        Assert.Equal(expected, suppress(boxes, .7).Length);
    }

    [Theory]
    [InlineData("V8")]
    [InlineData("V26")]
    public void Segmentation_ExclusiveEdgeKeepsLastMaskPixel(string version)
    {
        var type = typeof(YoloDotNet.Yolo).Assembly.GetType($"YoloDotNet.Modules.{version}.SegmentationModule{version}")!;
        var instance = System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(type);
        type.GetField("_maskWidth", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(instance, 160);
        type.GetField("_maskHeight", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(instance, 160);
        type.GetField("_scalingFactorW", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(instance, .25f);
        type.GetField("_scalingFactorH", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(instance, .25f);
        var box = (SKRectI)type.GetMethod("DownscaleBoundingBoxToSegmentationOutput", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(instance, [new SKRect(636, 636, 640, 640)])!;
        Assert.Equal(new SKRectI(159, 159, 160, 160), box);
    }

    [Fact]
    public void Sam_PromptAndFinalMaskUseSameOriginalPixel()
    {
        var p = Assert.Single(Snet.Yolo.Server.sam.SamOnnxRuntime.NormalizePrompts([new(500.9, 2.2)]));
        const int originalWidth = 1920, workWidth = 1024;
        var x = (int)(p.X * workWidth / originalWidth); var y = (int)(p.Y * 4 / 8);
        var mask = new byte[workWidth * 4]; mask[y * workWidth + x] = 255;
        var result = Snet.Yolo.Server.sam.SamMaskGeometry.Create(mask, workWidth, 4, originalWidth, 8, x, y, 1);
        Assert.Equal(255, result.Mask[(int)p.Y * originalWidth + (int)p.X]);
    }

    [Fact]
    public async Task Editor_ProjectSwitchReloadsProject_AndQueuedSaveKeepsOriginalIdentity()
    {
        var owner = "editor-" + Guid.NewGuid().ToString("N");
        await using var projects = new ProjectOperate(owner + "-projects");
        await using var tasks = new ProjectTaskOperate(owner + "-tasks");
        var workspace = new WorkspaceService(projects, tasks, null!, new CurrentUserContext(new TestUser(owner)), NullLogger<WorkspaceService>.Instance);
        var a = new WorkspaceProject { LabelConfigXml = YoloTaskRegistry.ConfigFor(YoloTaskType.Detect), Tasks = [new() { Id = 1, Data = new JsonObject { ["image"] = "a.png" } }] };
        var b = new WorkspaceProject { LabelConfigXml = a.LabelConfigXml, Tasks = [new() { Id = 1, Data = new JsonObject { ["image"] = "b.png" } }] };
        await workspace.SaveProjectAsync(a); await workspace.SaveProjectAsync(b);
        var editor = new Snet.Yolo.Tasks.Components.Pages.Editor { Language = new() };
        var type = editor.GetType();
        type.GetProperty("ProjectId")!.SetValue(editor, a.Id);
        type.GetProperty("Workspaces", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(editor, workspace);
        type.GetProperty("Toast", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(editor, new ToastService());
        Task Load() => (Task)type.GetMethod("OnParametersSetAsync", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(editor, null)!;
        try
        {
            await Load();
            var loadedA = (WorkspaceProject)type.GetField("_project", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(editor)!;
            loadedA.Tasks[0].Data!["savedFrom"] = "A";
            var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            type.GetField("_saveQueue", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(editor, gate.Task);
            type.GetMethod("QueueCurrentTaskSave", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(editor, null);
            type.GetProperty("ProjectId")!.SetValue(editor, b.Id);
            var switching = Load(); gate.SetResult(); await switching.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.Equal("b.png", type.GetField("_imageUrl", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(editor));
            Assert.Equal("A", (await workspace.GetProjectAsync(a.Id))!.Tasks[0].Data!["savedFrom"]!.ToString());
            Assert.Null((await workspace.GetProjectAsync(b.Id))!.Tasks[0].Data!["savedFrom"]);
        }
        finally
        {
            await editor.DisposeAsync();
            foreach (var id in new[] { a.Id, b.Id })
            {
                var query = await projects.QueryAsync(owner, id);
                if (query.GetDetails(out List<ProjectData>? rows) && rows is { Count: > 0 })
                { await projects.DeleteAggregateAsync(rows[0].id, owner, id); }
            }
        }
    }

    [Fact]
    public async Task Workflow_HostStopCancelsLinkedRun_AndWaitsForCompletion()
    {
        await using var workflow = new AnomalibWorkflowService(null!, null!);
        var type = workflow.GetType();
        var shutdown = (CancellationTokenSource)type.GetField("_shutdown", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(workflow)!;
        var stateType = type.GetNestedType("RunState", BindingFlags.NonPublic)!;
        var state = Activator.CreateInstance(stateType, "owner", "project", new Snet.Yolo.Server.anomalib.AnomalibTrainingOptions(), shutdown.Token)!;
        var cancellation = (CancellationTokenSource)stateType.GetProperty("Cancellation")!.GetValue(state)!;
        var completion = (TaskCompletionSource)stateType.GetProperty("Completion")!.GetValue(state)!;
        var runs = (System.Collections.IDictionary)type.GetField("_runs", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(workflow)!;
        runs.Add("owner\nproject", state);
        using var registration = cancellation.Token.Register(() => completion.TrySetResult());
        await workflow.StopAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(5));
        Assert.True(cancellation.IsCancellationRequested); Assert.True(completion.Task.IsCompletedSuccessfully);
        cancellation.Dispose();
    }

    /// <summary>输出契约桩经过真实 YoloCore/ModuleFactory/解析器，不执行神经网络计算。</summary>
    private sealed class PredictionProvider(bool endToEnd) : YoloDotNet.Models.Interfaces.IExecutionProvider
    {
        private readonly float[] _output = CreateOutput(endToEnd);
        public object Session => this;
        public YoloDotNet.Models.OnnxModel OnnxData { get; } = new()
        {
            ModelType = YoloDotNet.Enums.ModelType.ObjectDetection,
            ModelVersion = YoloDotNet.Enums.ModelVersion.V26,
            ModelDataType = YoloDotNet.Enums.ModelDataType.Float,
            Labels = [new() { Index = 0, Name = "defect" }],
            InputShapes = new() { ["images"] = [1, 3, 64, 64] }, InputShapeSize = 3 * 64 * 64,
            OutputShapes = new() { ["output0"] = endToEnd ? [1, 300, 6] : [1, 5, 3] }
        };
        public YoloDotNet.Core.InferenceResult Run<T>(T[] normalizedPixels) where T : unmanaged => new(_output, ReadOnlySpan<float>.Empty);
        public void Dispose() { }
        private static float[] CreateOutput(bool endToEnd)
        {
            if (endToEnd)
            {
                var data = new float[300 * 6]; data[0] = 10; data[1] = 20; data[2] = 30; data[3] = 40; data[4] = .9f;
                return data;
            }
            var raw = new float[5 * 3]; raw[0] = 20; raw[3] = 30; raw[6] = 20; raw[9] = 20; raw[12] = .9f;
            return raw;
        }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Yolo26_RawAndEndToEnd_UseCorrectParserAndCoordinates(bool endToEnd)
    {
        using var yolo = new YoloDotNet.Yolo(new YoloDotNet.Models.YoloOptions { ExecutionProvider = new PredictionProvider(endToEnd) });
        using var bitmap = new SKBitmap(64, 64); bitmap.Erase(SKColors.Black);
        var detection = Assert.Single(yolo.RunObjectDetection(bitmap, .5, .7));
        Assert.Equal("defect", detection.Label.Name);
        Assert.Equal(new SKRectI(10, 20, 30, 40), detection.BoundingBox);
        Assert.InRange(detection.Confidence, .89, .91);
    }
}
