using Snet.Yolo.Server.anomalib;
using Xunit;

namespace Snet.Yolo.Test;

public sealed class JointValidationMatcherTests
{
    [Fact]
    public void Match_AssignsDetectionToBestOverlappingAnomaly()
    {
        var regions = new[] { Region(1, 0, 0, 100, 100), Region(2, 70, 0, 100, 100) };
        var detection = new JointDetection("异物", 0.92, new PixelRectangle(80, 20, 60, 40));

        var result = JointValidationMatcher.Match(regions, [detection]);

        Assert.Empty(result.UnmatchedDetections);
        Assert.Empty(result.Regions[0].Detections);
        Assert.Same(detection, Assert.Single(result.Regions[1].Detections));
    }

    [Fact]
    public void Match_PreservesUnknownAnomalyAndYoloOnlyDetection()
    {
        var regions = new[] { Region(1, 0, 0, 40, 40) };
        var outside = new JointDetection("划痕", 0.8, new PixelRectangle(70, 70, 20, 20));

        var result = JointValidationMatcher.Match(regions, [outside]);

        Assert.Empty(Assert.Single(result.Regions).Detections);
        Assert.Same(outside, Assert.Single(result.UnmatchedDetections));
    }

    [Fact]
    public void Match_DoesNotAssignTinyEdgeIntersection()
    {
        var result = JointValidationMatcher.Match(
            [Region(1, 0, 0, 50, 50)],
            [new JointDetection("异物", 0.9, new PixelRectangle(49, 49, 30, 30))]);

        Assert.Empty(Assert.Single(result.Regions).Detections);
        Assert.Single(result.UnmatchedDetections);
    }

    private static AnomalibRegionResult Region(int id, int x, int y, int width, int height) => new()
    {
        RegionId = id,
        Bounds = new PixelRectangle(x, y, width, height),
        PixelArea = width * height,
        MaximumScore = 0.9f,
        MeanScore = 0.7f,
    };
}
