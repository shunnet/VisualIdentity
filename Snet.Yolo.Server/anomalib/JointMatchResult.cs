namespace Snet.Yolo.Server.anomalib;

/// <summary>已关联的异常区域及全部异常区域之外的 YOLO 结果。</summary>
public sealed record JointMatchResult(IReadOnlyList<JointRegionMatch> Regions, IReadOnlyList<JointDetection> UnmatchedDetections);
