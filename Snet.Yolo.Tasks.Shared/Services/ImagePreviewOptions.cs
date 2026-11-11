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

    /// <summary>
    /// 列表/网格缩略图的最长边（默认 360）。页面里的缩略位只有 54–200 个 CSS 像素，
    /// 用 1600 像素的预览图去填它们既浪费带宽（现场网络只有 1–2 MB/s），也浪费浏览器的解码时间。
    /// </summary>
    public int ThumbMaxEdge { get; set; } = 360;
    /// <summary>缩略图起始 JPEG 质量。</summary>
    public int ThumbQuality { get; set; } = 72;
    /// <summary>缩略图允许的最大字节数（默认 32 KiB，实际通常 15–30 KiB）。</summary>
    public long ThumbTargetBytes { get; set; } = 32L * 1024;
    /// <summary>同时解码大图的并发上限（默认 2）：5120×5120 的位图解码后约 105 MB，并发过多会挤占内存与 CPU。</summary>
    public int MaxConcurrentGenerations { get; set; } = 2;
}
