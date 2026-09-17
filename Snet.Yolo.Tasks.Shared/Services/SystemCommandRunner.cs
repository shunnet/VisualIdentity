namespace Snet.Yolo.Tasks.Services;

/// <summary>ISystemCommandRunner 的真实实现：复用 TrainingShell（流式输出、卡死检测、关闭 stdin）。</summary>
public sealed class SystemCommandRunner : ISystemCommandRunner
{
    /// <summary>流式执行命令。</summary>
    public Task<(int ExitCode, string Tail, bool Stalled)> RunStreamingAsync(string file, IReadOnlyList<string> arguments, Action<string> onOutput, TimeSpan stallTimeout, CancellationToken cancellationToken)
        => TrainingShell.RunStreamingAsync(file, arguments, null, null, onOutput, stallTimeout, cancellationToken);

    /// <summary>文件是否存在。</summary>
    public bool FileExists(string path) => File.Exists(path);

    /// <summary>在 PATH 中查找可执行文件。</summary>
    public string? FindOnPath(string executableName)
    {
        var name = OperatingSystem.IsWindows() && !executableName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
            ? executableName + ".exe"
            : executableName;
        foreach (var directory in (Environment.GetEnvironmentVariable("PATH") ?? string.Empty).Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            try
            {
                var candidate = Path.Combine(directory.Trim().Trim('"'), name);
                if (File.Exists(candidate)) { return Path.GetFullPath(candidate); }
            }
            catch (ArgumentException) { /* PATH 里的非法路径直接跳过 */ }
        }
        return null;
    }

    /// <summary>读取进程自身的有效用户 id（Linux/macOS），用于判断是否需要 sudo。</summary>
    public static bool IsRoot()
    {
        try
        {
            if (!OperatingSystem.IsWindows()) { return Environment.IsPrivilegedProcess; }
        }
        catch (PlatformNotSupportedException) { }
        return false;
    }

    /// <summary>可执行文件是否存在于绝对路径（Linux 常见安装位置探测用）。</summary>
    public static string? FirstExisting(params string[] candidates)
    {
        foreach (var candidate in candidates)
        {
            if (!string.IsNullOrWhiteSpace(candidate) && File.Exists(candidate)) { return candidate; }
        }
        return null;
    }
}
