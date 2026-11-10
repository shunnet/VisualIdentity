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
