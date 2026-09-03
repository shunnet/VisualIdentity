using System;
using System.Linq;

namespace Snet.Yolo.Tasks.Core.Config;

/// <summary>YOLO 任务类型（Ultralytics 五类：detect/segment/classify/pose/obb）。</summary>
public enum YoloTaskType { Detect, Segment, Classify, Pose, Obb }

/// <summary>配置控制类型 <-> YOLO 任务 的双向映射。</summary>
public static class YoloTaskRegistry
{
    /// <summary>由配置推断 YOLO 任务（按配置中出现的控制类型优先级）。</summary>
    public static YoloTaskType FromConfig(LabelingConfigModel config)
    {
        var kinds = config.Controls.Select(c => c.Kind).ToList();
        if (kinds.Contains(ControlTagKind.KeyPointLabels)) return YoloTaskType.Pose;
        if (kinds.Contains(ControlTagKind.PolygonLabels) || kinds.Contains(ControlTagKind.BrushLabels)) return YoloTaskType.Segment;
        if (kinds.Contains(ControlTagKind.Choices) || kinds.Contains(ControlTagKind.Labels)) return YoloTaskType.Classify;
        if (kinds.Contains(ControlTagKind.EllipseLabels)) return YoloTaskType.Obb;
        return YoloTaskType.Detect;
    }

    public static string ToCommand(YoloTaskType task) => task switch
    {
        YoloTaskType.Segment => "segment",
        YoloTaskType.Classify => "classify",
        YoloTaskType.Pose => "pose",
        YoloTaskType.Obb => "obb",
        _ => "detect",
    };

    /// <summary>按任务给基础模型名追加后缀（yolo26n.pt -> yolo26n-seg.pt / -cls / -pose / -obb）。</summary>
    public static string ModelFor(YoloTaskType task, string baseModel)
    {
        if (task == YoloTaskType.Detect) { return baseModel; }
        var suffix = ModelSuffix(task);
        if (baseModel.EndsWith(".pt", StringComparison.Ordinal) && !baseModel.Contains("-" + suffix, StringComparison.Ordinal))
        {
            return baseModel[..^3] + "-" + suffix + ".pt";
        }
        return baseModel;
    }

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
        YoloTaskType.Pose => "\n<View>\n  <Image name=\"image\" value=\"" + imageField + "\"/>\n  <KeyPointLabels name=\"kp\" toName=\"image\"></KeyPointLabels>\n</View>\n",
        YoloTaskType.Obb => "\n<View>\n  <Image name=\"image\" value=\"" + imageField + "\"/>\n  <RectangleLabels name=\"rect\" toName=\"image\"></RectangleLabels>\n</View>\n",
        _ => "\n<View>\n  <Image name=\"image\" value=\"" + imageField + "\"/>\n  <RectangleLabels name=\"rect\" toName=\"image\"></RectangleLabels>\n</View>\n",
    };
}
