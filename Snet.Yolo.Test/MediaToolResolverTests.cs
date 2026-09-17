using Microsoft.Extensions.Options;
using Snet.Yolo.Tasks.Services;
using Xunit;

namespace Snet.Yolo.Test;

/// <summary>验证媒体工具路径在不同发布方式下的解析行为，以及"安装记录 / 手动指定"的生效链路。</summary>
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
            var resolver = new MediaToolResolver(Options.Create(new MediaToolOptions { FFmpegPath = directory }), NewStore());

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
        var resolver = new MediaToolResolver(Options.Create(new MediaToolOptions { FFmpegPath = missing }), NewStore());

        var exception = Assert.Throws<InvalidOperationException>(() => resolver.GetPaths());

        Assert.Contains(missing, exception.Message);
    }

    /// <summary>自检不能抛异常：找不到时必须返回 false + 可读原因（视频上传流程依赖它）。</summary>
    [Fact]
    public void TryGetPaths_ReportsMissingToolWithoutThrowing()
    {
        var resolver = new MediaToolResolver(Options.Create(new MediaToolOptions { FFmpegPath = Missing() }), NewStore());

        var ok = resolver.TryGetPaths(out var paths, out var error);

        Assert.False(ok);
        Assert.Null(paths);
        Assert.False(string.IsNullOrWhiteSpace(error));
    }

    /// <summary>安装/手动指定记录的路径优先于自动发现，且 Refresh() 后立刻换到新的安装位置。</summary>
    [Fact]
    public void RecordedPath_WinsOverAutoDiscovery_AndRefreshPicksUpNewInstall()
    {
        var first = NewDirectory();
        var second = NewDirectory();
        try
        {
            var store = NewStore();
            WriteFakeTools(first);
            store.Save(new MediaToolSettings { FFmpegPath = first, Source = "manual" });
            var resolver = new MediaToolResolver(Options.Create(new MediaToolOptions()), store);

            Assert.True(resolver.TryGetPaths(out var paths, out _));
            Assert.Equal(Path.Combine(first, ExecutableName("ffmpeg")), paths!.FFmpeg);

            // 重新安装到别处：Refresh 之前仍用缓存，Refresh 之后切到新位置
            WriteFakeTools(second);
            store.Save(new MediaToolSettings { FFmpegPath = second, Source = "download" });
            Assert.Equal(Path.Combine(first, ExecutableName("ffmpeg")), resolver.GetPaths().FFmpeg);

            resolver.Refresh();
            Assert.Equal(Path.Combine(second, ExecutableName("ffmpeg")), resolver.GetPaths().FFmpeg);
        }
        finally
        {
            Directory.Delete(first, true);
            Directory.Delete(second, true);
        }
    }

    /// <summary>手动指定路径时，ffmpeg 与 ffprobe 必须都到位（否则视频解析会在中途失败）。</summary>
    [Fact]
    public void TryResolveUserPath_RequiresBothExecutables()
    {
        var directory = NewDirectory();
        try
        {
            File.WriteAllBytes(Path.Combine(directory, ExecutableName("ffmpeg")), Array.Empty<byte>());
            Assert.False(MediaToolResolver.TryResolveUserPath(directory, out _, out _, out var missingProbe));
            Assert.Contains("ffprobe", missingProbe!, StringComparison.OrdinalIgnoreCase);

            File.WriteAllBytes(Path.Combine(directory, ExecutableName("ffprobe")), Array.Empty<byte>());
            Assert.True(MediaToolResolver.TryResolveUserPath(directory, out var ffmpeg, out var ffprobe, out _));
            Assert.Equal(Path.Combine(directory, ExecutableName("ffmpeg")), ffmpeg);
            Assert.Equal(Path.Combine(directory, ExecutableName("ffprobe")), ffprobe);
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    /// <summary>解压包常见的 bin 子目录也要能识别。</summary>
    [Fact]
    public void TryResolveUserPath_AcceptsBinSubdirectory()
    {
        var directory = NewDirectory();
        try
        {
            var bin = Path.Combine(directory, "bin");
            Directory.CreateDirectory(bin);
            WriteFakeTools(bin);

            Assert.True(MediaToolResolver.TryResolveUserPath(directory, out var ffmpeg, out _, out _));

            Assert.Equal(Path.Combine(bin, ExecutableName("ffmpeg")), ffmpeg);
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    internal static string ExecutableName(string baseName) => OperatingSystem.IsWindows() ? baseName + ".exe" : baseName;

    internal static void WriteFakeTools(string directory)
    {
        Directory.CreateDirectory(directory);
        File.WriteAllBytes(Path.Combine(directory, ExecutableName("ffmpeg")), Array.Empty<byte>());
        File.WriteAllBytes(Path.Combine(directory, ExecutableName("ffprobe")), Array.Empty<byte>());
    }

    internal static string NewDirectory()
    {
        var directory = Path.Combine(Path.GetTempPath(), "snet-media-tools", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        return directory;
    }

    /// <summary>独立设置文件，避免测试之间互相影响。</summary>
    internal static MediaToolSettingsStore NewStore()
        => new(Path.Combine(Path.GetTempPath(), "snet-media-tools", Guid.NewGuid().ToString("N"), "media-tools.json"));

    internal static string Missing() => Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "ffmpeg");
}
