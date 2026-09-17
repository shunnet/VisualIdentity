namespace Snet.Yolo.Tasks.Services;

/// <summary>FFmpeg 安装流程的阶段。</summary>
public enum FfmpegInstallPhase
{
    /// <summary>未开始（工具已可用时会停在这里）。</summary>
    Idle,
    /// <summary>正在自检部署环境。</summary>
    Checking,
    /// <summary>正在下载安装包（仅 Windows 静默安装）。</summary>
    Downloading,
    /// <summary>正在解压安装包。</summary>
    Extracting,
    /// <summary>正在执行安装命令（Linux 包管理器）。</summary>
    Installing,
    /// <summary>正在校验安装结果。</summary>
    Verifying,
    /// <summary>安装完成。</summary>
    Completed,
    /// <summary>用户主动取消（不是错误：界面不再显示横幅，只给一条提示）。</summary>
    Cancelled,
    /// <summary>安装失败（界面需要提示，且提供手动指定路径作为兜底）。</summary>
    Failed,
}

/// <summary>FFmpeg 安装状态快照（供界面渲染进度横幅 / 弹窗）。</summary>
public sealed record FfmpegInstallState(
    FfmpegInstallPhase Phase,
    string Message,
    int? Percent,
    string? Error,
    string? InstalledPath,
    IReadOnlyList<string> LogTail)
{
    /// <summary>是否正在执行安装类任务（用于显示进度横幅）。</summary>
    public bool IsBusy => Phase is FfmpegInstallPhase.Checking or FfmpegInstallPhase.Downloading
        or FfmpegInstallPhase.Extracting or FfmpegInstallPhase.Installing or FfmpegInstallPhase.Verifying;

    /// <summary>是否需要在页面上显示横幅（进行中或失败待处理）。</summary>
    public bool IsVisible => IsBusy || Phase == FfmpegInstallPhase.Failed;

    /// <summary>进度条百分比（无精确进度时返回 null，界面按不确定进度展示）。</summary>
    public int? BarPercent => Percent is int percent ? Math.Clamp(percent, 0, 100) : null;

    /// <summary>初始状态。</summary>
    public static FfmpegInstallState Idle { get; } = new(FfmpegInstallPhase.Idle, string.Empty, null, null, null, Array.Empty<string>());
}

/// <summary>下载 FFmpeg 安装包（抽出来便于单测：测试里用假下载器产出压缩包）。</summary>
public interface IFfmpegDownloader
{
    /// <summary>下载最新版 FFmpeg 压缩包到指定目录。</summary>
    /// <param name="destinationDirectory">压缩包落地目录。</param>
    /// <param name="onProgress">已下载字节数与总字节数（总长未知时为 null）。</param>
    /// <param name="cancellationToken">取消标记。</param>
    /// <returns>压缩包完整路径与版本号。</returns>
    Task<(string ArchivePath, string Version)> DownloadLatestAsync(string destinationDirectory, Action<long, long?> onProgress, CancellationToken cancellationToken);
}

/// <summary>执行系统命令（抽出来便于单测：测试里用假执行器模拟 apt 安装）。</summary>
public interface ISystemCommandRunner
{
    /// <summary>流式执行命令，把输出逐行回调给调用方。</summary>
    Task<(int ExitCode, string Tail, bool Stalled)> RunStreamingAsync(string file, IReadOnlyList<string> arguments, Action<string> onOutput, TimeSpan stallTimeout, CancellationToken cancellationToken);

    /// <summary>文件是否存在。</summary>
    bool FileExists(string path);

    /// <summary>在 PATH 中查找可执行文件，找不到返回 null。</summary>
    string? FindOnPath(string executableName);
}
