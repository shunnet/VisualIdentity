using Snet.Yolo.Tasks.Services;
using Xunit;

namespace Snet.Yolo.Test;

/// <summary>
/// 识别结果面板的数据口径：
/// 视频按标签聚合（平均置信度 + 全片识别次数，不含坐标），照片保持"每个目标一行且带坐标"。
/// </summary>
public sealed class ValidationDetectionAggregationTests
{
    [Fact]
    public void VideoAggregation_AveragesConfidenceAndCountsPerLabel()
    {
        var aggregated = ValidationService.AggregateDetections(new[]
        {
            ("纸", 0.83, "{Left=165,Top=1,Width=2,Height=3}"),
            ("纸", 0.58, "{Left=167,Top=1,Width=2,Height=3}"),
            ("梗", 0.52, "{Left=132,Top=1,Width=2,Height=3}"),
            ("纸", 0.35, "{Left=842,Top=1,Width=2,Height=3}"),
        });

        Assert.Equal(2, aggregated.Count);
        Assert.Equal("纸", aggregated[0].Name);                 // 次数多的在前
        Assert.Equal(3, aggregated[0].Count);
        Assert.Equal("59%", aggregated[0].Confidence);          // (0.83+0.58+0.35)/3 ≈ 0.587
        Assert.Equal(string.Empty, aggregated[0].Position);     // 视频不再输出坐标
        Assert.Equal("梗", aggregated[1].Name);
        Assert.Equal(1, aggregated[1].Count);
        Assert.Equal("52%", aggregated[1].Confidence);
    }

    [Fact]
    public void VideoAggregation_SortsByCountThenAverageConfidence()
    {
        var aggregated = ValidationService.AggregateDetections(new[]
        {
            ("A", 0.90, ""),                       // 1 次 → 最后
            ("B", 0.40, ""), ("B", 0.40, ""),      // 2 次，平均 0.40
            ("C", 0.30, ""), ("C", 0.70, ""),      // 2 次，平均 0.50 → 同样次数时平均高的在前
            ("D", 0.20, ""), ("D", 0.30, ""),      // 2 次，平均 0.25
        });

        Assert.Equal(new[] { "C", "B", "D", "A" }, aggregated.Select(item => item.Name));
    }

    [Fact]
    public void VideoAggregation_HandlesEmptyAndSingleFrameResults()
    {
        Assert.Empty(ValidationService.AggregateDetections(Array.Empty<(string, double, string)>()));

        var single = ValidationService.AggregateDetections(new[] { ("纸", 0.5, "{Left=1}") });
        Assert.Single(single);
        Assert.Equal(1, single[0].Count);
        Assert.Equal("50%", single[0].Confidence);
    }

    [Fact]
    public void PhotoDetection_DefaultsToSingleOccurrence()
    {
        // 照片路径用 3 参构造，Count 默认为 1（界面据此展示坐标而不是次数）
        var detection = new ValidationDetection("纸", "83%", "{Left=165,Top=1,Width=2,Height=3}");

        Assert.Equal(1, detection.Count);
        Assert.NotEmpty(detection.Position);
    }
}
