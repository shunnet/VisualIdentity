using System.Net;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Snet.Yolo.Server.anomalib;
using Snet.Yolo.Tasks.Components.Shell;
using Snet.Yolo.Tasks.Core.Localization;
using Xunit;

namespace Snet.Yolo.Test;

/// <summary>真实 Razor 校准报告的双语、百分比、分页和下载入口验证。</summary>
public sealed class AnomalibCalibrationReportTests
{
    /// <summary>中文和英文均如实显示 50% 通过与误报，并提供独立下载而不自动添加模型。</summary>
    [Theory]
    [InlineData("zh-CN", "校准未通过", "下载校准报告")]
    [InlineData("en-US", "Calibration not passed", "Download calibration report")]
    public async Task Report_RendersCountsPercentagesAndManualDownload(string culture, string status, string download)
    {
        var report = AnomalibParityResult.Parse(AnomalibCalibrationExportTests.FailedReport);
        Assert.Equal(0.5, report.PassRate);
        Assert.Equal(0.5, report.NormalFalsePositiveRate);
        Assert.Equal(1, report.FailedSampleCount);
        var html = await RenderAsync(report, culture);
        var decoded = WebUtility.HtmlDecode(html);
        Assert.Contains(status, decoded);
        Assert.Contains(download, decoded);
        Assert.Contains("1 / 2 · 50%", decoded);
        Assert.Contains("one.png", decoded);
        Assert.Contains("two.png", decoded);
        Assert.Contains("/api/anomalib/training/project/anomalib-run-11111111111111111111111111111111/download", html);
        Assert.Contains("/api/anomalib/training/project/anomalib-run-11111111111111111111111111111111/report", html);
        Assert.DoesNotContain("AnomalibCalibrationDownloadHint", html);
        await File.WriteAllTextAsync(Path.Combine(AppContext.BaseDirectory, $"calibration-report-{culture}.html"), html, TestContext.Current.CancellationToken);
    }

    /// <summary>大量详情只渲染当前 50 行，文件名和错误原文经过 Razor 编码。</summary>
    [Fact]
    public async Task Report_BoundsRenderedRowsAndEscapesDiagnosticText()
    {
        var report = new AnomalibParityResult
        {
            ExportSucceeded = true, TotalSampleCount = 105, SampleCount = 105, NormalSampleCount = 105,
            SampleResults = Enumerable.Range(1, 105).Select(i => new AnomalibCalibrationSampleResult
            {
                Index = i, FileName = $"sample-{i:D3}.png", Message = "<script>alert(1)</script>",
            }).ToArray(),
        };
        var html = await RenderAsync(report, "en-US");
        Assert.Equal(51, Regex.Matches(html, "<tr(?: |>)").Count);
        Assert.Contains("sample-050.png", html);
        Assert.DoesNotContain("sample-051.png", html);
        Assert.DoesNotContain("<script>", html);
        Assert.Contains("Page 1 / 3, 105 images", WebUtility.HtmlDecode(html));
    }

    /// <summary>使用实际 Razor 组件渲染，保留产品的本地化、属性编码和条件显示行为。</summary>
    /// <param name="report">报告数据。</param>
    /// <param name="culture">目标语言。</param>
    /// <returns>组件生成的 HTML。</returns>
    private static async Task<string> RenderAsync(AnomalibParityResult report, string culture)
    {
        await using var services = new ServiceCollection().AddLogging().AddScoped<LanguageManager>().BuildServiceProvider();
        services.GetRequiredService<LanguageManager>().SetLanguage(culture);
        await using var renderer = new HtmlRenderer(services, services.GetRequiredService<ILoggerFactory>());
        return await renderer.Dispatcher.InvokeAsync(async () =>
        {
            var result = await renderer.RenderComponentAsync<AnomalibCalibrationReport>(ParameterView.FromDictionary(new Dictionary<string, object?>
            {
                [nameof(AnomalibCalibrationReport.Report)] = report,
                [nameof(AnomalibCalibrationReport.ProjectId)] = "project",
                [nameof(AnomalibCalibrationReport.RunId)] = "anomalib-run-11111111111111111111111111111111",
            }));
            return result.ToHtmlString();
        });
    }
}
