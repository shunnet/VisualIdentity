namespace Snet.Yolo.Server.anomalib;

/// <summary>原图像素坐标中的 YOLO 检测结果。</summary>
public sealed record JointDetection(string Name, double Confidence, PixelRectangle Bounds);

/// <summary>异常区域及与其空间关联的已知缺陷。</summary>
public sealed record JointRegionMatch(AnomalibRegionResult Region, IReadOnlyList<JointDetection> Detections);

/// <summary>已关联的异常区域及全部异常区域之外的 YOLO 结果。</summary>
public sealed record JointMatchResult(IReadOnlyList<JointRegionMatch> Regions, IReadOnlyList<JointDetection> UnmatchedDetections);

/// <summary>每个 YOLO 检测最多关联一个 Anomalib 区域。</summary>
public static class JointValidationMatcher
{
    /// <summary>依据中心点或目标框重叠比例关联原图坐标区域。</summary>
    public static JointMatchResult Match(IReadOnlyList<AnomalibRegionResult> regions, IReadOnlyList<JointDetection> detections)
    {
        ArgumentNullException.ThrowIfNull(regions);
        ArgumentNullException.ThrowIfNull(detections);

        var matched = regions.Select(_ => new List<JointDetection>()).ToArray();
        var unmatched = new List<JointDetection>();
        foreach (var detection in detections)
        {
            var bestIndex = -1;
            var bestOverlap = 0L;
            for (var i = 0; i < regions.Count; i++)
            {
                var bounds = regions[i].Bounds;
                var overlapWidth = Math.Max(0, Math.Min(bounds.Right, detection.Bounds.Right) - Math.Max(bounds.X, detection.Bounds.X));
                var overlapHeight = Math.Max(0, Math.Min(bounds.Bottom, detection.Bounds.Bottom) - Math.Max(bounds.Y, detection.Bounds.Y));
                var overlap = (long)overlapWidth * overlapHeight;
                var centerX = detection.Bounds.X + detection.Bounds.Width / 2;
                var centerY = detection.Bounds.Y + detection.Bounds.Height / 2;
                var centerInside = centerX >= bounds.X && centerX < bounds.Right && centerY >= bounds.Y && centerY < bounds.Bottom;
                if ((centerInside || overlap * 10 >= (long)detection.Bounds.Width * detection.Bounds.Height * 3) && overlap > bestOverlap)
                {
                    bestIndex = i;
                    bestOverlap = overlap;
                }
            }

            if (bestIndex < 0) { unmatched.Add(detection); }
            else { matched[bestIndex].Add(detection); }
        }

        return new JointMatchResult(
            regions.Select((region, index) => new JointRegionMatch(region, matched[index])).ToArray(),
            unmatched);
    }
}
