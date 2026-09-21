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

/// <summary>配置控制类型与 YOLO 任务之间的双向映射。</summary>
public static class YoloTaskRegistry
{
    /// <summary>由配置推断 YOLO 任务（按配置中出现的控制类型优先级）。</summary>
    public static YoloTaskType FromConfig(LabelingConfigModel config)
    {
        if (string.Equals(config.YoloTask, "obb", StringComparison.OrdinalIgnoreCase)) return YoloTaskType.Obb;
        var kinds = config.Controls.Select(c => c.Kind).ToList();
        if (kinds.Contains(ControlTagKind.KeyPointLabels)) return YoloTaskType.Pose;
        if (kinds.Contains(ControlTagKind.PolygonLabels) || kinds.Contains(ControlTagKind.BrushLabels)) return YoloTaskType.Segment;
        if (kinds.Contains(ControlTagKind.Choices) || kinds.Contains(ControlTagKind.Labels)) return YoloTaskType.Classify;
        if (kinds.Contains(ControlTagKind.EllipseLabels)) return YoloTaskType.Obb;
        return YoloTaskType.Detect;
    }

    /// <summary>Returns the Ultralytics CLI task name.</summary>
    public static string ToCommand(YoloTaskType task) => task switch
    {
        YoloTaskType.Segment => "segment",
        YoloTaskType.Classify => "classify",
        YoloTaskType.Pose => "pose",
        YoloTaskType.Obb => "obb",
        _ => "detect",
    };

    /// <summary>将模型名归一化为指定任务的官方权重名称。</summary>
    /// <remarks>切换任务时会先移除已有任务后缀，避免生成 <c>yolo26n-pose-seg.pt</c> 之类的无效名称。</remarks>
    public static string ModelFor(YoloTaskType task, string baseModel)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(baseModel);
        if (!baseModel.EndsWith(".pt", StringComparison.OrdinalIgnoreCase)) { return baseModel; }

        var stem = baseModel[..^3];
        foreach (var knownSuffix in KnownModelSuffixes)
        {
            if (!stem.EndsWith(knownSuffix, StringComparison.OrdinalIgnoreCase)) { continue; }
            stem = stem[..^knownSuffix.Length];
            break;
        }

        var suffix = ModelSuffix(task);
        return stem + (suffix.Length == 0 ? string.Empty : "-" + suffix) + ".pt";
    }

    private static readonly string[] KnownModelSuffixes = ["-seg", "-cls", "-pose", "-obb"];

    /// <summary>Returns the official model filename suffix for a task.</summary>
    public static string ModelSuffix(YoloTaskType task) => task switch
    {
        YoloTaskType.Segment => "seg",
        YoloTaskType.Classify => "cls",
        YoloTaskType.Pose => "pose",
        YoloTaskType.Obb => "obb",
        _ => "",
    };

    /// <summary>为指定 YOLO 任务生成基础配置（仅含吻合的控制，编辑器据此自动启用对应工具）。</summary>
    public static string ConfigFor(YoloTaskType task, string imageField = "$image") => task switch
    {
        YoloTaskType.Segment => "\n<View>\n  <Image name=\"image\" value=\"" + imageField + "\"/>\n  <PolygonLabels name=\"poly\" toName=\"image\"></PolygonLabels>\n  <BrushLabels name=\"mask\" toName=\"image\"></BrushLabels>\n</View>\n",
        YoloTaskType.Classify => "\n<View>\n  <Image name=\"image\" value=\"" + imageField + "\"/>\n  <Choices name=\"choice\" toName=\"image\"></Choices>\n</View>\n",
        YoloTaskType.Pose => "\n<View>\n  <Image name=\"image\" value=\"" + imageField + "\"/>\n  <RectangleLabels name=\"object\" toName=\"image\"></RectangleLabels>\n  <KeyPointLabels name=\"kp\" toName=\"image\"></KeyPointLabels>\n</View>\n",
        YoloTaskType.Obb => "\n<View yoloTask=\"obb\">\n  <Image name=\"image\" value=\"" + imageField + "\"/>\n  <RectangleLabels name=\"rect\" toName=\"image\"></RectangleLabels>\n</View>\n",
        _ => "\n<View>\n  <Image name=\"image\" value=\"" + imageField + "\"/>\n  <RectangleLabels name=\"rect\" toName=\"image\"></RectangleLabels>\n</View>\n",
    };
}
