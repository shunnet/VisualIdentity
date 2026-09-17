using Microsoft.Extensions.Options;
using System.Runtime.InteropServices;

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

/// <summary>当前平台已解析的媒体工具绝对路径。</summary>
public sealed record MediaToolPaths(string FFmpeg, string FFprobe);

/// <summary>
/// 按"配置 → 环境变量 → 安装记录 → 应用自带目录 → PATH/常见安装位置"的顺序解析跨平台 FFmpeg 工具。
/// 解析结果缓存；安装完成后调用 <see cref="Refresh"/> 让新装的工具立刻生效。
/// </summary>
public sealed class MediaToolResolver
{
    private readonly MediaToolOptions _options;
    private readonly MediaToolSettingsStore _settings;
    private volatile MediaToolPaths? _cached;

    /// <summary>创建媒体工具解析器，解析结果在应用生命周期内缓存（安装后需 Refresh）。</summary>
    public MediaToolResolver(IOptions<MediaToolOptions> options, MediaToolSettingsStore settings)
    {
        _options = options.Value;
        _settings = settings;
    }

    /// <summary>获取当前平台可用的 FFmpeg 和 FFprobe 绝对路径；找不到时抛出带处理建议的异常。</summary>
    public MediaToolPaths GetPaths() => _cached ??= ResolvePaths();

    /// <summary>探测工具是否可用（不抛异常），供"上传视频前自检"使用。</summary>
    /// <param name="paths">解析到的路径，失败时为 null。</param>
    /// <param name="error">失败原因，成功时为 null。</param>
    public bool TryGetPaths(out MediaToolPaths? paths, out string? error)
    {
        try
        {
            paths = GetPaths();
            error = null;
            return true;
        }
        catch (Exception ex)
        {
            paths = null;
            error = ex.Message;
            return false;
        }
    }

    /// <summary>清空缓存，使下一次解析重新查找（安装/手动指定路径后调用）。</summary>
    public void Refresh() => _cached = null;

    /// <summary>当前平台对应的自带目录名，如 win-x64 / linux-x64 / osx-arm64。</summary>
    public string PlatformDirectoryName
    {
        get
        {
            var platform = OperatingSystem.IsWindows() ? "win" : OperatingSystem.IsLinux() ? "linux" : OperatingSystem.IsMacOS() ? "osx" : "unknown";
            return platform + "-" + RuntimeInformation.OSArchitecture.ToString().ToLowerInvariant();
        }
    }

    /// <summary>本次安装应使用的目录（比 自带目录名 更精确，例如 tools/ffmpeg/win-x64）。</summary>
    public string ResolvePlatformInstallDirectory() => Path.Combine(_options.ResolveInstallDirectory(), PlatformDirectoryName);

    /// <summary>同时解析两个配套工具，使 FFprobe 可以自动使用 FFmpeg 的同级目录。</summary>
    private MediaToolPaths ResolvePaths()
    {
        var recorded = _settings.Load();
        var ffmpeg = ResolveExecutable("ffmpeg", _options.FFmpegPath, "SNET_FFMPEG_PATH", recorded.FFmpegPath, null);
        var ffprobe = ResolveExecutable("ffprobe", _options.FFprobePath, "SNET_FFPROBE_PATH", recorded.FFprobePath, Path.GetDirectoryName(ffmpeg));
        return new MediaToolPaths(ffmpeg, ffprobe);
    }

    /// <summary>按确定的优先级查找可执行文件，且不通过 shell 执行用户文本。</summary>
    private string ResolveExecutable(string baseName, string? configuredPath, string environmentVariable, string? recordedPath, string? siblingDirectory)
    {
        var executableName = OperatingSystem.IsWindows() ? baseName + ".exe" : baseName;
        var explicitPath = string.IsNullOrWhiteSpace(configuredPath) ? Environment.GetEnvironmentVariable(environmentVariable) : configuredPath;
        if (!string.IsNullOrWhiteSpace(explicitPath))
        {
            var resolvedExplicitPath = ResolveFileOrDirectory(explicitPath, executableName);
            if (resolvedExplicitPath is not null) { return resolvedExplicitPath; }
            throw new InvalidOperationException($"已配置 {baseName} 路径，但文件不存在: {explicitPath}");
        }

        // 安装记录（手动指定或自动安装）优先于自动发现
        if (!string.IsNullOrWhiteSpace(recordedPath))
        {
            var resolvedRecordedPath = ResolveFileOrDirectory(recordedPath, executableName);
            if (resolvedRecordedPath is not null) { return resolvedRecordedPath; }
        }

        foreach (var directory in CandidateDirectories(siblingDirectory))
        {
            var candidate = Path.Combine(directory, executableName);
            if (File.Exists(candidate)) { return Path.GetFullPath(candidate); }
        }

        var platform = $"{RuntimeInformation.OSDescription.Trim()} / {RuntimeInformation.OSArchitecture}";
        throw new InvalidOperationException(
            $"当前平台 {platform} 未找到 {baseName}。" +
            $"请安装 FFmpeg，或设置 MediaTools:{(baseName == "ffmpeg" ? "FFmpegPath" : "FFprobePath")}，" +
            $"也可设置环境变量 {environmentVariable}。");
    }

    /// <summary>
    /// 校验"用户手动填写"的 FFmpeg 路径：可以是 ffmpeg 可执行文件，也可以是包含它的目录（含解压常见的 bin 子目录）。
    /// FFprobe 取同目录下的配套文件。
    /// </summary>
    /// <param name="path">用户填写的路径。</param>
    /// <param name="ffmpeg">解析出的 FFmpeg 绝对路径。</param>
    /// <param name="ffprobe">解析出的 FFprobe 绝对路径。</param>
    /// <param name="error">失败原因。</param>
    public static bool TryResolveUserPath(string path, out string ffmpeg, out string ffprobe, out string? error)
    {
        ffmpeg = string.Empty;
        ffprobe = string.Empty;
        error = null;
        if (string.IsNullOrWhiteSpace(path))
        {
            error = "请填写 FFmpeg 路径。";
            return false;
        }
        var resolvedFfmpeg = ResolveFileOrDirectory(path, OperatingSystem.IsWindows() ? "ffmpeg.exe" : "ffmpeg");
        if (resolvedFfmpeg is null)
        {
            error = "该路径下没有找到 " + (OperatingSystem.IsWindows() ? "ffmpeg.exe" : "ffmpeg") + "：" + path;
            return false;
        }
        var resolvedFfprobe = ResolveFileOrDirectory(Path.GetDirectoryName(resolvedFfmpeg) ?? string.Empty, OperatingSystem.IsWindows() ? "ffprobe.exe" : "ffprobe");
        if (resolvedFfprobe is null)
        {
            error = "同一目录下没有找到 " + (OperatingSystem.IsWindows() ? "ffprobe.exe" : "ffprobe") + "（FFprobe 与 FFmpeg 需要放在一起）。";
            return false;
        }
        ffmpeg = resolvedFfmpeg;
        ffprobe = resolvedFfprobe;
        return true;
    }

    /// <summary>把显式配置的文件或目录转换为已存在的绝对路径。</summary>
    private static string? ResolveFileOrDirectory(string path, string executableName)
    {
        if (string.IsNullOrWhiteSpace(path)) { return null; }
        var expanded = Environment.ExpandEnvironmentVariables(path.Trim().Trim('"'));
        if (File.Exists(expanded)) { return Path.GetFullPath(expanded); }
        if (!Directory.Exists(expanded)) { return null; }
        var candidate = Path.Combine(expanded, executableName);
        if (File.Exists(candidate)) { return Path.GetFullPath(candidate); }
        // 压缩包解压后常见的一层 bin 子目录
        var binCandidate = Path.Combine(expanded, "bin", executableName);
        return File.Exists(binCandidate) ? Path.GetFullPath(binCandidate) : null;
    }

    /// <summary>列举安装目录、应用自带、系统 PATH 和常见安装位置，并去除重复项。</summary>
    private IEnumerable<string> CandidateDirectories(string? siblingDirectory)
    {
        var installRoot = _options.ResolveInstallDirectory();
        var candidates = new List<string?>
        {
            siblingDirectory,
            ResolvePlatformInstallDirectory(),
            Path.Combine(installRoot, PlatformDirectoryName, "bin"),
            installRoot,
            Path.Combine(installRoot, "bin"),
            Path.Combine(AppContext.BaseDirectory, "tools", "ffmpeg", PlatformDirectoryName),
            Path.Combine(AppContext.BaseDirectory, "tools", "ffmpeg"),
            AppContext.BaseDirectory,
        };
        // 系统级发现（PATH / WinGet / 常见安装位置）：关闭时只认安装目录，便于固定工具集
        if (_options.DiscoverInstalledTools)
        {
            candidates.AddRange((Environment.GetEnvironmentVariable("PATH") ?? string.Empty).Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries));
            if (OperatingSystem.IsWindows())
            {
                candidates.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Microsoft", "WinGet", "Links"));
                candidates.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "ffmpeg", "bin"));
            }
            else
            {
                candidates.AddRange(new[] { "/usr/bin", "/usr/local/bin", "/snap/bin", "/opt/homebrew/bin" });
            }
        }
        return candidates
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Select(path => path!.Trim().Trim('"'))
            .Distinct(StringComparer.OrdinalIgnoreCase);
    }
}
