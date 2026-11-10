namespace Snet.Yolo.Tasks.Services;

/// <summary>视频处理工具的强类型配置。</summary>
public sealed class MediaToolOptions
{
    /// <summary>配置节名。</summary>
    public const string SectionName = "MediaTools";

    /// <summary>FFmpeg 可执行文件或所在目录；留空时自动发现。</summary>
    public string? FFmpegPath { get; init; }

    /// <summary>FFprobe 可执行文件或所在目录；留空时自动发现。</summary>
    public string? FFprobePath { get; init; }

    /// <summary>自动安装目录（Windows 解压位置、Linux 安装后记录位置）；默认部署目录下 tools/ffmpeg。</summary>
    public string? InstallDirectory { get; init; }

    /// <summary>查询最新版本的接口地址（企业镜像可替换）；默认 GitHub GyanD/codexffmpeg 的最新发布。</summary>
    public string? ReleaseApiUrl { get; init; }

    /// <summary>
    /// 是否自动发现系统里已安装的 FFmpeg（PATH、WinGet、Program Files、/usr/bin 等）。默认开启；
    /// 关闭后只认显式配置、安装记录与安装目录，便于固定使用某一套工具（测试也用它保证确定性）。
    /// </summary>
    public bool DiscoverInstalledTools { get; init; } = true;

    /// <summary>实际生效的安装目录。</summary>
    public string ResolveInstallDirectory() => string.IsNullOrWhiteSpace(InstallDirectory)
        ? Path.Combine(AppContext.BaseDirectory, "tools", "ffmpeg")
        : Environment.ExpandEnvironmentVariables(InstallDirectory.Trim().Trim('"'));

    /// <summary>实际生效的版本查询地址。</summary>
    public string ResolveReleaseApiUrl() => string.IsNullOrWhiteSpace(ReleaseApiUrl)
        ? DefaultReleaseApiUrl
        : ReleaseApiUrl.Trim();

    /// <summary>默认的版本查询地址。</summary>
    public const string DefaultReleaseApiUrl = "https://api.github.com/repos/GyanD/codexffmpeg/releases/latest";
}
