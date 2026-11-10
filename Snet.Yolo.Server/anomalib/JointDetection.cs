namespace Snet.Yolo.Server.anomalib;

/// <summary>原图像素坐标中的 YOLO 检测结果。</summary>
public sealed record JointDetection(string Name, double Confidence, PixelRectangle Bounds);
