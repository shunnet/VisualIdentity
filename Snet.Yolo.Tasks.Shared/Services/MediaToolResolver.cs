using System.Runtime.InteropServices;
using Microsoft.Extensions.Options;

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
}

/// <summary>当前平台已解析的媒体工具绝对路径。</summary>
public sealed record MediaToolPaths(string FFmpeg, string FFprobe);

/// <summary>
/// 按配置、环境变量、应用自带目录和 PATH 的顺序解析跨平台 FFmpeg 工具。
/// </summary>
public sealed class MediaToolResolver
{
    private readonly MediaToolOptions _options;
    private readonly Lazy<MediaToolPaths> _paths;

    /// <summary>创建媒体工具解析器，解析结果在应用生命周期内缓存。</summary>
    public MediaToolResolver(IOptions<MediaToolOptions> options)
    {
        _options = options.Value;
        _paths = new Lazy<MediaToolPaths>(ResolvePaths, LazyThreadSafetyMode.ExecutionAndPublication);
    }

    /// <summary>获取当前平台可用的 FFmpeg 和 FFprobe 绝对路径。</summary>
    public MediaToolPaths GetPaths() => _paths.Value;

    /// <summary>同时解析两个配套工具，使 FFprobe 可以自动使用 FFmpeg 的同级目录。</summary>
    private MediaToolPaths ResolvePaths()
    {
        var ffmpeg = ResolveExecutable("ffmpeg", _options.FFmpegPath, "SNET_FFMPEG_PATH", null);
        var ffprobe = ResolveExecutable("ffprobe", _options.FFprobePath, "SNET_FFPROBE_PATH", Path.GetDirectoryName(ffmpeg));
        return new MediaToolPaths(ffmpeg, ffprobe);
    }

    /// <summary>按确定的优先级查找可执行文件，且不通过 shell 执行用户文本。</summary>
    private static string ResolveExecutable(string baseName, string? configuredPath, string environmentVariable, string? siblingDirectory)
    {
        var executableName = OperatingSystem.IsWindows() ? baseName + ".exe" : baseName;
        var explicitPath = string.IsNullOrWhiteSpace(configuredPath) ? Environment.GetEnvironmentVariable(environmentVariable) : configuredPath;
        if (!string.IsNullOrWhiteSpace(explicitPath))
        {
            var resolvedExplicitPath = ResolveFileOrDirectory(explicitPath, executableName);
            if (resolvedExplicitPath is not null) { return resolvedExplicitPath; }
            throw new InvalidOperationException($"已配置 {baseName} 路径，但文件不存在: {explicitPath}");
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

    /// <summary>把显式配置的文件或目录转换为已存在的绝对路径。</summary>
    private static string? ResolveFileOrDirectory(string path, string executableName)
    {
        var expanded = Environment.ExpandEnvironmentVariables(path.Trim().Trim('"'));
        if (File.Exists(expanded)) { return Path.GetFullPath(expanded); }
        if (!Directory.Exists(expanded)) { return null; }
        var candidate = Path.Combine(expanded, executableName);
        return File.Exists(candidate) ? Path.GetFullPath(candidate) : null;
    }

    /// <summary>列举应用自带、系统 PATH 和常见安装位置，并去除重复项。</summary>
    private static IEnumerable<string> CandidateDirectories(string? siblingDirectory)
    {
        var architecture = RuntimeInformation.OSArchitecture.ToString().ToLowerInvariant();
        var platform = OperatingSystem.IsWindows() ? "win" : OperatingSystem.IsLinux() ? "linux" : OperatingSystem.IsMacOS() ? "osx" : "unknown";
        var runtimeDirectory = Path.Combine(AppContext.BaseDirectory, "tools", "ffmpeg", $"{platform}-{architecture}");
        var candidates = new List<string?>
        {
            siblingDirectory,
            runtimeDirectory,
            Path.Combine(AppContext.BaseDirectory, "tools", "ffmpeg"),
            AppContext.BaseDirectory,
        };
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
        return candidates
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Select(path => path!.Trim().Trim('"'))
            .Distinct(StringComparer.OrdinalIgnoreCase);
    }
}
