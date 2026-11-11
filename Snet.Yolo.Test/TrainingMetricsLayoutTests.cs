using System.Text.RegularExpressions;
using Xunit;

namespace Snet.Yolo.Test;

/// <summary>
/// 训练指标页的版式约束：表格单元格为不换行，展开行里的运行信息（失败时可能是一整段 CUDA / Python 日志）
/// 必须在卡片内换行，不能撑宽整张表。这类回归只有浏览器里才看得见，所以静态检查样式与标记的接线。
/// </summary>
public sealed class TrainingMetricsLayoutTests
{
    /// <summary>读取样式表中某个选择器的声明块；找不到时直接失败。</summary>
    private static string Rule(string css, string selector)
    {
        var match = Regex.Match(css, Regex.Escape(selector) + @"\s*\{(?<body>[^}]*)\}", RegexOptions.CultureInvariant);
        Assert.True(match.Success, $"样式表里没有 {selector} 规则。");
        return match.Groups["body"].Value;
    }

    /// <summary>展开行单元格恢复换行，内容宽度跟随表格而不参与列宽计算，运行信息按字符换行并限制高度。</summary>
    [Fact]
    public void MetricsDetailRow_WrapsLongMessagesInsteadOfStretchingTheTable()
    {
        var css = File.ReadAllText(Path.Combine(RepositoryRoot(), "Snet.Yolo.Tasks.Shared", "wwwroot", "css", "ls.css"));

        Assert.Contains("white-space: nowrap", Rule(css, ".ls-metrics-table td"));
        Assert.Contains("white-space: normal", Rule(css, ".ls-metrics-detail > td"));
        var body = Rule(css, ".ls-metrics-detail-body");
        Assert.Contains("width: 0", body);
        Assert.Contains("min-width: 100%", body);
        var message = Rule(css, ".ls-metrics-message");
        Assert.Contains("white-space: pre-wrap", message);
        Assert.Contains("overflow-wrap: anywhere", message);
        Assert.Contains("max-height", message);
        Assert.Contains("overflow-y: auto", message);
    }

    /// <summary>页面确实把详情包进 ls-metrics-detail-body，并给运行信息加上 ls-metrics-message。</summary>
    [Fact]
    public void MetricsPage_UsesTheWrappingClasses()
    {
        var page = File.ReadAllText(Path.Combine(RepositoryRoot(), "Snet.Yolo.Tasks.Shared", "Components", "Pages", "TrainingMetrics.razor"));

        Assert.Contains("\"ls-metrics-detail-body\"", page);
        Assert.Contains("ls-metrics-message", page);
        Assert.Contains("<tr @key=\"detailKey\" class=\"ls-metrics-detail\">", page);
    }

    /// <summary>从测试输出目录向上找到仓库根目录。</summary>
    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !Directory.Exists(Path.Combine(directory.FullName, "Snet.Yolo.Tasks.Shared")))
        {
            directory = directory.Parent;
        }
        return directory?.FullName ?? throw new DirectoryNotFoundException("找不到 VisualIdentity 仓库根目录。");
    }
}
