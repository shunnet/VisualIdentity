using Microsoft.Extensions.Options;
using Snet.Yolo.Tasks.Services;
using Xunit;

namespace Snet.Yolo.Test;

/// <summary>验证媒体工具路径在不同发布方式下的解析行为。</summary>
public sealed class MediaToolResolverTests
{
    /// <summary>确保配置一个包含配套工具的目录即可跨平台解析。</summary>
    [Fact]
    public void ConfiguredDirectory_ResolvesBothExecutables()
    {
        var directory = Path.Combine(Path.GetTempPath(), "snet-media-tools", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var extension = OperatingSystem.IsWindows() ? ".exe" : string.Empty;
            var ffmpeg = Path.Combine(directory, "ffmpeg" + extension);
            var ffprobe = Path.Combine(directory, "ffprobe" + extension);
            File.WriteAllBytes(ffmpeg, Array.Empty<byte>());
            File.WriteAllBytes(ffprobe, Array.Empty<byte>());
            var resolver = new MediaToolResolver(Options.Create(new MediaToolOptions { FFmpegPath = directory }));

            var paths = resolver.GetPaths();

            Assert.Equal(Path.GetFullPath(ffmpeg), paths.FFmpeg);
            Assert.Equal(Path.GetFullPath(ffprobe), paths.FFprobe);
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    /// <summary>确保管理员填写错误的显式路径时立即返回可诊断错误。</summary>
    [Fact]
    public void MissingConfiguredPath_ReturnsActionableError()
    {
        var missing = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "ffmpeg");
        var resolver = new MediaToolResolver(Options.Create(new MediaToolOptions { FFmpegPath = missing }));

        var exception = Assert.Throws<InvalidOperationException>(() => resolver.GetPaths());

        Assert.Contains(missing, exception.Message);
    }
}
