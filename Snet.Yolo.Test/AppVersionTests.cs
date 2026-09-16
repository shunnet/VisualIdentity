using Snet.Yolo.Tasks.Core;
using Xunit;

namespace Snet.Yolo.Test;

/// <summary>界面左上角展示的程序版本号（宿主程序集 &lt;Version&gt;）。</summary>
public sealed class AppVersionTests
{
    [Theory]
    [InlineData("1.0.1.1", "1.0.1.1")]
    [InlineData("1.0.1.1+9f2c1a4", "1.0.1.1")]   // SourceLink 会追加 +commit 后缀
    [InlineData("  1.0.1.1  ", "1.0.1.1")]
    [InlineData("1.0.0.0+local", "1.0.0.0")]
    [InlineData("", "")]
    [InlineData("   ", "")]
    [InlineData(null, "")]
    public void Format_TrimsWhitespaceAndCommitSuffix(string? raw, string expected)
    {
        Assert.Equal(expected, AppVersion.Format(raw));
    }

    [Fact]
    public void Resolve_ReadsVersionFromAssembly()
    {
        var version = AppVersion.Resolve(typeof(AppVersion).Assembly);

        Assert.False(string.IsNullOrWhiteSpace(version));
        Assert.DoesNotContain('+', version);
        Assert.True(Version.TryParse(version, out _), "版本号应可解析为 Version：" + version);
    }

    [Fact]
    public void Resolve_ReturnsEmptyWhenAssemblyMissing()
    {
        Assert.Equal(string.Empty, AppVersion.Resolve(null));
    }

    [Fact]
    public void Current_NeverContainsCommitSuffix()
    {
        // 测试宿主的版本信息与宿主程序不同，这里只保证取到的文本是干净的、且不抛异常
        Assert.DoesNotContain('+', AppVersion.Current);
    }
}
