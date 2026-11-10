namespace Snet.Yolo.Tasks.Core.Config;

/// <summary>控制（标注）标签种类：对应 LS control tags。</summary>
public enum ControlTagKind
{
    /// <summary>未识别。</summary>
    Unknown = 0,
    /// <summary>Rectangle labels.</summary>
    RectangleLabels,
    /// <summary>Polygon labels.</summary>
    PolygonLabels,
    /// <summary>Keypoint labels.</summary>
    KeyPointLabels,
    /// <summary>Ellipse labels.</summary>
    EllipseLabels,
    /// <summary>Brush-mask labels.</summary>
    BrushLabels,
    /// <summary>Generic region labels.</summary>
    Labels,
    /// <summary>Choice controls.</summary>
    Choices,
    /// <summary>Rating controls.</summary>
    Rating,
    /// <summary>Free-text controls.</summary>
    TextArea,
    /// <summary>Taxonomy controls.</summary>
    Taxonomy,
    /// <summary>Region relations.</summary>
    Relation,
    /// <summary>Numeric controls.</summary>
    Number,
    /// <summary>Pairwise comparison controls.</summary>
    Pairwise,
    /// <summary>Ranking controls.</summary>
    Ranker,
    /// <summary>Date and time controls.</summary>
    DateTime,
    /// <summary>Keyboard shortcuts.</summary>
    Shortcut,
}
