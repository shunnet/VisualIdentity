namespace Snet.Yolo.Tasks.Core.Config;

/// <summary>YOLO 任务类型（Ultralytics 五类：detect/segment/classify/pose/obb）。</summary>
public enum YoloTaskType
{
    /// <summary>Axis-aligned object detection.</summary>
    Detect,
    /// <summary>Instance segmentation.</summary>
    Segment,
    /// <summary>Image classification.</summary>
    Classify,
    /// <summary>Pose estimation.</summary>
    Pose,
    /// <summary>Oriented bounding-box detection.</summary>
    Obb,
}
