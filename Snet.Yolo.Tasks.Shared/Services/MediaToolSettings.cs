namespace Snet.Yolo.Tasks.Services;

/// <summary>媒体工具（FFmpeg / FFprobe）的用户设置：记住手动指定或自动安装后的位置。</summary>
public sealed class MediaToolSettings
{
    /// <summary>FFmpeg 可执行文件或其所在目录。</summary>
    public string? FFmpegPath { get; set; }

    /// <summary>FFprobe 可执行文件或其所在目录。</summary>
    public string? FFprobePath { get; set; }

    /// <summary>中文字体文件路径（视频标注绘制中文标签用；SkiaSharp 默认字体没有中文字形）。</summary>
    public string? CjkFontPath { get; set; }

    /// <summary>来源：manual（用户指定）/ download（Windows 静默下载安装）/ package（Linux 包管理器安装）。</summary>
    public string? Source { get; set; }

    /// <summary>最后更新时间。</summary>
    public DateTimeOffset? UpdatedAt { get; set; }

    /// <summary>是否记录过任何路径。</summary>
    public bool HasValue => !string.IsNullOrWhiteSpace(FFmpegPath) || !string.IsNullOrWhiteSpace(FFprobePath);
}
