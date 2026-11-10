namespace Snet.Yolo.Tasks.Services;

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
