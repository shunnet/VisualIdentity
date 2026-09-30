using Snet.Yolo.Server.anomalib;
using Snet.Yolo.Server.models.@enum;
using Snet.Yolo.Tasks.Services;
using Xunit;

namespace Snet.Yolo.Test;

public sealed class AnomalibValidationStateTests
{
    [Fact]
    public void NewValidationState_DefaultsToPointEight_WithoutOverwritingSavedThreshold()
    {
        var anomalib = new AnomalibValidationState();
        var joint = new JointValidationState();
        Assert.Equal(0.8f, anomalib.Get("alice").PixelThreshold);
        Assert.Equal(0.8f, joint.Get("alice").PixelThreshold);
        anomalib.Save("alice", anomalib.Get("alice") with { PixelThreshold = 0.6f });
        joint.Save("alice", joint.Get("alice") with { PixelThreshold = 0.7f });
        Assert.Equal(0.6f, anomalib.Get("alice").PixelThreshold);
        Assert.Equal(0.7f, joint.Get("alice").PixelThreshold);
    }

    [Fact]
    public void Refresh_RestoresModelMediaResultsLogsAndFilters_PerUser()
    {
        var state = new AnomalibValidationState();
        var file = new AnomalibValidationMedia
        {
            Path = "a.png",
            Url = "/uploads/a.png",
            Name = "a.png",
            IsVideo = false,
            Output = Output(),
            VideoResultUrl = "/uploads/result.mp4",
            ResultMessageKey = "AnomalibVideoComplete",
            ResultMessageArguments = [10, 2],
        };
        state.Save("alice", new("project/run", file.Url, [file], [new("AnomalibImageComplete", [1])], 0.8f, 50));
        file.Output = null;
        file.ResultMessageArguments[0] = 99;

        var restored = state.Get("alice");
        Assert.Equal("project/run", restored.SelectedModelKey);
        Assert.Equal(file.Url, restored.SelectedFileUrl);
        Assert.Equal(0.8f, restored.PixelThreshold);
        Assert.Equal(50, restored.MinimumArea);
        Assert.Single(Assert.Single(restored.Files).Output!.Result.Regions);
        Assert.Equal("/uploads/result.mp4", restored.Files[0].VideoResultUrl);
        Assert.Equal(10, restored.Files[0].ResultMessageArguments[0]);
        Assert.Single(restored.Logs);
        restored.Files[0].Output = null;
        restored.Logs[0].Arguments[0] = 99;
        Assert.NotNull(state.Get("alice").Files[0].Output);
        Assert.Equal(1, state.Get("alice").Logs[0].Arguments[0]);
        Assert.Empty(state.Get("bob").Files);
        Assert.Throws<ArgumentException>(() => state.Get(""));
    }

    [Fact]
    public void Save_RemovalAndBoundedLogs_ArePreserved()
    {
        var state = new AnomalibValidationState();
        state.Save("alice", new("project/run", null, [], Enumerable.Range(0, 1100).Select(i => new AnomalibValidationLog("log", [i])).ToArray(), 0.5f, 4));
        Assert.Equal(1000, state.Get("alice").Logs.Count);
        state.Save("alice", state.Get("alice") with { SelectedModelKey = "", Logs = [] });
        Assert.Empty(state.Get("alice").Logs);
        Assert.Equal("", state.Get("alice").SelectedModelKey);
    }

    [Fact]
    public void JointRefresh_RestoresModesTimingParametersAndIsolatedResults()
    {
        var state = new JointValidationState();
        var region = Output().Result.Regions[0];
        var detections = new List<JointDetection> { new("defect", 0.9, region.Bounds) };
        var matches = new JointMatchResult([new(region, detections)], []);
        var parameters = YoloValidationParameters.Create(OnnxType.Segmentation);
        parameters["Confidence"] = 0.8;
        var images = new List<JointValidationImage>
        {
            new("a", "a.png", "/a.png", "a.png", 32, 32, "/a.preview.jpg", new(Output(), matches, JointValidationMode.Joint, true, 123)),
            new("b", "b.png", "/b.png", "b.png", 64, 64, "/b.png", Error: "failed"),
        };
        state.Save("alice", new("project/run", "42", images, "a", JointValidationMode.YoloOnly, ["done"], 0.7f, 30, parameters));
        images.Clear();
        detections.Clear();
        parameters["Confidence"] = 0.1;

        var restored = state.Get("alice");
        Assert.Equal("42", restored.YoloModelIndex);
        Assert.Equal("a", restored.SelectedImageId);
        Assert.Equal(JointValidationMode.YoloOnly, restored.Mode);
        Assert.Equal(2, restored.Images.Count);
        Assert.Equal("a.png", restored.Images[0].Path);
        Assert.Equal("/a.preview.jpg", restored.Images[0].PreviewUrl);
        Assert.Equal(123, restored.Images[0].Output!.YoloMilliseconds);
        Assert.Equal(JointValidationMode.Joint, restored.Images[0].Output!.Mode);
        Assert.Single(restored.Images[0].Output!.Matches.Regions[0].Detections);
        Assert.Null(restored.Images[1].Output);
        Assert.Equal("failed", restored.Images[1].Error);
        var restoredDetections = (JointDetection[])restored.Images[0].Output!.Matches.Regions[0].Detections;
        restoredDetections[0] = new("modified", 0, region.Bounds);
        Assert.Equal("defect", state.Get("alice").Images[0].Output!.Matches.Regions[0].Detections[0].Name);
        Assert.Equal(0.8, restored.YoloParameters["Confidence"]);
        Assert.Equal(0.7f, restored.PixelThreshold);
        Assert.Equal(30, restored.MinimumArea);
        Assert.Single(restored.Logs);
        Assert.Empty(state.Get("bob").Images);
    }

    [Fact]
    public void JointImages_RemovalSelectionAndLimits_ArePreserved()
    {
        var state = new JointValidationState();
        var image = new JointValidationImage("a", "a.png", "/a.png", "a.png", 32, 32, "/a.png");
        state.Save("alice", state.Get("alice") with { Images = [image], SelectedImageId = image.Id });
        state.Save("alice", state.Get("alice") with { Images = [], SelectedImageId = null });
        Assert.Empty(state.Get("alice").Images);
        Assert.Null(state.Get("alice").SelectedImageId);
        Assert.Equal(JointValidationMode.Joint, state.Get("alice").Mode);
        var maximum = Enumerable.Range(0, JointValidationState.MaximumImages).Select(i => image with { Id = i.ToString() }).ToArray();
        state.Save("alice", state.Get("alice") with { Images = maximum });
        Assert.Equal(JointValidationState.MaximumImages, state.Get("alice").Images.Count);
        Assert.Throws<ArgumentException>(() => state.Save("alice", state.Get("alice") with { Images = [.. maximum, image] }));
        Assert.Equal(JointValidationState.MaximumImages, state.Get("alice").Images.Count);
    }

    [Fact]
    public void YoloParameters_DynamicallyMatchValidationModelType()
    {
        Assert.Equal(["Confidence", "Iou"], YoloValidationParameters.Create(OnnxType.ObjectDetection).Keys);
        var segmentation = YoloValidationParameters.Create(OnnxType.Segmentation);
        Assert.Equal(["Confidence", "Iou", "PixelConfidence"], segmentation.Keys);
        segmentation["PixelConfidence"] = 0.9;
        var data = System.Text.Json.JsonSerializer.Deserialize<Snet.Yolo.Server.models.data.SegmentationData>(System.Text.Json.JsonSerializer.Serialize(segmentation))!;
        Assert.Equal(0.9, data.PixelConfidence);
        Assert.Equal(["Classes"], YoloValidationParameters.Create(OnnxType.Classification).Keys);
    }

    private static AnomalibInferenceOutput Output() => new()
    {
        HeatmapDataUrl = "data:image/png;base64,test",
        Result = new AnomalibImageResult
        {
            ImageScore = 0.7f,
            IsAnomalous = true,
            OriginalWidth = 32,
            OriginalHeight = 32,
            MapWidth = 8,
            MapHeight = 8,
            InferenceMilliseconds = 10,
            Regions = [new() { RegionId = 1, Bounds = new(0, 0, 4, 4), PixelArea = 4, MaximumScore = 0.9f, MeanScore = 0.7f }],
        },
    };
}
