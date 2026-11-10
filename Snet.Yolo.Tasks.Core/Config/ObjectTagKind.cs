namespace Snet.Yolo.Tasks.Core.Config;

/// <summary>对象（数据展示）标签种类：对应 LS object tags。</summary>
public enum ObjectTagKind
{
    /// <summary>未识别。</summary>
    Unknown = 0,
    /// <summary>Image data.</summary>
    Image,
    /// <summary>Plain text data.</summary>
    Text,
    /// <summary>Audio data.</summary>
    Audio,
    /// <summary>Time-series data.</summary>
    TimeSeries,
    /// <summary>Video data.</summary>
    Video,
    /// <summary>HTML data.</summary>
    HyperText,
    /// <summary>Paragraph data.</summary>
    Paragraphs,
}
