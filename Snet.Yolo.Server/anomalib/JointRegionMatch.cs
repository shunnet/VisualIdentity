namespace Snet.Yolo.Server.anomalib;

/// <summary>异常区域及与其空间关联的已知缺陷。</summary>
public sealed record JointRegionMatch(AnomalibRegionResult Region, IReadOnlyList<JointDetection> Detections);
