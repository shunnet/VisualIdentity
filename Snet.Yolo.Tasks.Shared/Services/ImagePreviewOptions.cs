namespace Snet.Yolo.Tasks.Services;

/// <summary>验证页图片优化选项（配置节 <c>Images:Preview</c>）。</summary>
public sealed class ImagePreviewOptions
{
    /// <summary>是否启用（默认启用）。</summary>
    public bool Enabled { get; set; } = true;
    /// <summary>预览图允许的最大字节数（默认 400 KiB）。</summary>
    public long TargetBytes { get; set; } = 400L * 1024;
    /// <summary>预览图最长边上限（默认 1600）。超过则等比缩小；设为 0 表示不限制尺寸。</summary>
    public int MaxEdge { get; set; } = 1600;
    /// <summary>起始 JPEG 质量。</summary>
    public int StartQuality { get; set; } = 82;
    /// <summary>最低 JPEG 质量（再低就靠缩尺寸达标）。</summary>
    public int MinQuality { get; set; } = 60;
}
