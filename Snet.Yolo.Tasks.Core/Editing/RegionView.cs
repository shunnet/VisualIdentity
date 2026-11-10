namespace Snet.Yolo.Tasks.Core.Editing;

using System;

/// <summary>画布推送的区域视图（几何按类型承载；颜色由服务端解析）。</summary>
public sealed class RegionView
{
    /// <summary>区域 id。</summary>
    public string Id { get; set; } = string.Empty;
    /// <summary>region 类型（RegionType 常量：rectanglelabels/polygonlabels/keypointlabels/ellipselabels）。</summary>
    public string Type { get; set; } = string.Empty;
    /// <summary>标签文本（用于回显）。</summary>
    public string LabelText { get; set; } = string.Empty;
    /// <summary>颜色。</summary>
    public string Color { get; set; } = "#40a0ff";
    /// <summary>是否选中。</summary>
    public bool Selected { get; set; }

    // ── rectanglelabels ──
    /// <summary>Rectangle left coordinate in pixels.</summary>
    public double X { get; set; }
    /// <summary>Rectangle top coordinate in pixels.</summary>
    public double Y { get; set; }
    /// <summary>Rectangle width in pixels.</summary>
    public double Width { get; set; }
    /// <summary>Rectangle height in pixels.</summary>
    public double Height { get; set; }
    /// <summary>Clockwise rotation in degrees.</summary>
    public double Rotation { get; set; }

    // ── polygonlabels（像素点序列） ──
    /// <summary>Polygon or brush X coordinates in pixels.</summary>
    public double[] PointsX { get; set; } = Array.Empty<double>();
    /// <summary>Polygon or brush Y coordinates in pixels.</summary>
    public double[] PointsY { get; set; } = Array.Empty<double>();
    /// <summary>各边的像素控制点 [x1,y1,x2,y2]；null 表示直线。</summary>
    public double[]?[] Curves { get; set; } = Array.Empty<double[]?>();

    // ── keypointlabels ──
    /// <summary>Keypoint X coordinate in pixels.</summary>
    public double Kx { get; set; }
    /// <summary>Keypoint Y coordinate in pixels.</summary>
    public double Ky { get; set; }

    // ── brushlabels（点列 + 笔宽，用于描边渲染） ──
    /// <summary>Brush diameter in pixels.</summary>
    public double BrushSize { get; set; }
    /// <summary>实心掩码预览；原始精度保存在 RLE 中。</summary>
    public string? MaskDataUrl { get; set; }

    // ── ellipselabels（中心 + 半径） ──
    /// <summary>Ellipse center X coordinate in pixels.</summary>
    public double Ex { get; set; }
    /// <summary>Ellipse center Y coordinate in pixels.</summary>
    public double Ey { get; set; }
    /// <summary>Ellipse horizontal radius in pixels.</summary>
    public double Rx { get; set; }
    /// <summary>Ellipse vertical radius in pixels.</summary>
    public double Ry { get; set; }
}
