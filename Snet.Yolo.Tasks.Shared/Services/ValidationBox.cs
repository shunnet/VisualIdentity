using Snet.Yolo.Server.anomalib;

namespace Snet.Yolo.Tasks.Services;

/// <summary>传给前端绘制器的原图坐标、标签及线条样式。</summary>
public sealed record ValidationBox(PixelRectangle Bounds, string Label, string Color, bool Dashed = false)
{
    /// <summary>异常框显示区域编号及结果列表中对应的峰值分数。</summary>
    public static ValidationBox FromRegion(AnomalibRegionResult region, string regionFormat)
        => new(region.Bounds, $"{string.Format(regionFormat, region.RegionId)} {region.MaximumScore:0.###}", "#fff");

    /// <summary>YOLO 框显示类别和百分比置信度，联合验证可区分未匹配的目标。</summary>
    public static ValidationBox FromDetection(JointDetection detection, bool dashed = false)
        => new(detection.Bounds, $"{detection.Name} {Math.Round(detection.Confidence * 100, MidpointRounding.AwayFromZero):0}%", "#ff3b30", dashed);
}
