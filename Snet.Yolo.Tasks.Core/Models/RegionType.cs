namespace Snet.Yolo.Tasks.Core.Models;

/// <summary>
/// Label Studio region 类型字符串常量（与 result[].type 及导出格式一一对应）。
/// </summary>
public static class RegionType
{
    /// <summary>矩形框（RectangleLabels）。</summary>
    public const string RectangleLabels = "rectanglelabels";
    /// <summary>多边形（PolygonLabels）。</summary>
    public const string PolygonLabels = "polygonlabels";
    /// <summary>关键点（KeyPointLabels）。</summary>
    public const string KeyPointLabels = "keypointlabels";
    /// <summary>椭圆（EllipseLabels）。</summary>
    public const string EllipseLabels = "ellipselabels";
    /// <summary>笔刷掩码（BrushLabels）。</summary>
    public const string BrushLabels = "brushlabels";
    /// <summary>文本/音频区间标签（Labels）。</summary>
    public const string Labels = "labels";
    /// <summary>分类选择（Choices）。</summary>
    public const string Choices = "choices";
    /// <summary>文本框（TextArea）。</summary>
    public const string TextArea = "textarea";
    /// <summary>评分（Rating）。</summary>
    public const string Rating = "rating";
    /// <summary>层级标签（Taxonomy）。</summary>
    public const string Taxonomy = "taxonomy";
    /// <summary>区域间关系（Relation）。</summary>
    public const string Relation = "relation";
    /// <summary>数值（Number）。</summary>
    public const string Number = "number";
}
