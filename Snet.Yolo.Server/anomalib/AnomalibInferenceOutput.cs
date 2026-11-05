namespace Snet.Yolo.Server.anomalib;

/// <summary>一次异常检测的结构化结果及低分辨率热图。</summary>
public sealed class AnomalibInferenceOutput
{
    /// <summary>整图分数及原图坐标的异常区域。</summary>
    public required AnomalibImageResult Result { get; init; }
    /// <summary>透明 PNG 热图的 data URL。</summary>
    public required string HeatmapDataUrl { get; init; }
}
