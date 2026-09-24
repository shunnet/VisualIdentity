using Snet.Yolo.Tasks.Services;
using Xunit;

namespace Snet.Yolo.Test;

/// <summary>验证 YOLO 和 Anomalib 训练页只返回真实且允许的站内入口。</summary>
public sealed class TrainingReturnNavigationTests
{
    /// <summary>从项目列表或详情进入训练页时，返回目标保留原来的查询参数。</summary>
    [Theory]
    [InlineData("/projects?page=2", "/train/abc", false)]
    [InlineData("/project/abc?tab=models", "/train/abc", false)]
    [InlineData("/anomalib-projects?page=3", "/anomalib-train/abc", true)]
    [InlineData("/anomalib-project/abc?tab=images", "/anomalib-train/abc", true)]
    public void WithSource_Resolve_ReturnsActualEntry(string entry, string trainPath, bool anomalib)
    {
        var url = TrainingReturnNavigation.WithSource(trainPath, "https://localhost:5001" + entry);
        var encodedSource = url.Split("returnTo=", 2)[1];
        Assert.Equal(entry, TrainingReturnNavigation.Resolve(Uri.UnescapeDataString(encodedSource), "abc", anomalib));
    }

    /// <summary>直接访问训练页或伪造跨站来源时仅回到对应项目列表。</summary>
    [Theory]
    [InlineData(null, false, "/projects")]
    [InlineData("https://example.com", false, "/projects")]
    [InlineData("//example.com", true, "/anomalib-projects")]
    [InlineData("/anomalib-project/other", true, "/anomalib-projects")]
    [InlineData("/project/abc\\evil", false, "/projects")]
    public void Resolve_RejectsMissingOrInvalidEntry(string? entry, bool anomalib, string expected)
        => Assert.Equal(expected, TrainingReturnNavigation.Resolve(entry, "abc", anomalib));
}
