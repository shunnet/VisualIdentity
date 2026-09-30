namespace Snet.Yolo.Server.sam;

/// <summary>原图像素坐标的交互点；Include=false 表示排除该位置。</summary>
/// <param name="X">横坐标。</param><param name="Y">纵坐标。</param><param name="Include">是否包含。</param>
public sealed record SamPrompt(double X, double Y, bool Include = true);

/// <summary>单张图片的编码结果，仅由当前标注页面持有，不进入跨用户缓存。</summary>
public sealed class SamImageContext
{
    /// <summary>编码时锁定的文件版本；更新后仍使用原解码器，避免特征与权重混用。</summary>
    internal SamInstalledModel? Installed { get; init; }
    /// <summary>编码所使用的模型，切换模型时不得复用另一模型的编码。</summary>
    public SamModelKind ModelKind { get; internal init; }
    /// <summary>编码使用的 GPU 编号；null 表示 CPU，解码沿用同一设备。</summary>
    public int? GpuId { get; internal init; }
    internal float[] Embedding { get; init; } = [];
    internal float[] HighRes0 { get; init; } = [];
    internal float[] HighRes1 { get; init; } = [];
    internal int WorkWidth { get; init; }
    internal int WorkHeight { get; init; }
    /// <summary>原图宽。</summary>
    public int Width { get; internal init; }
    /// <summary>原图高。</summary>
    public int Height { get; internal init; }
}

/// <summary>SAM 原图坐标结果；掩膜为单通道 0/255，轮廓不重复闭合点。</summary>
public sealed class SamResult
{
    /// <summary>原图宽。</summary>
    public required int Width { get; init; }
    /// <summary>原图高。</summary>
    public required int Height { get; init; }
    /// <summary>原图掩膜。</summary>
    public required byte[] Mask { get; init; }
    /// <summary>轮廓横坐标。</summary>
    public required double[] PointsX { get; init; }
    /// <summary>轮廓纵坐标。</summary>
    public required double[] PointsY { get; init; }
    /// <summary>外接矩形左边。</summary>
    public required double X { get; init; }
    /// <summary>外接矩形上边。</summary>
    public required double Y { get; init; }
    /// <summary>外接矩形宽。</summary>
    public required double BoxWidth { get; init; }
    /// <summary>外接矩形高。</summary>
    public required double BoxHeight { get; init; }
    /// <summary>解码器质量评分，不是类别置信度。</summary>
    public required float Score { get; init; }
    /// <summary>透明预览图，缩小后的掩膜仅用于显示。</summary>
    public required string PreviewDataUrl { get; init; }
}
