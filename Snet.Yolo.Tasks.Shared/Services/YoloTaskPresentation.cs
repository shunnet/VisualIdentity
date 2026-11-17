using Snet.Yolo.Tasks.Core.Config;

namespace Snet.Yolo.Tasks.Services;

/// <summary>为工程列表、详情与训练页统一解析 YOLO 类型和显示信息。</summary>
public static class YoloTaskPresentation
{
    /// <summary>沿用训练的配置解析规则，优先识别 OBB 标记；空或损坏配置回退为普通检测。</summary>
    public static YoloTaskType Parse(string? xml)
    {
        try { return YoloTaskRegistry.FromConfig(LabelingConfigParser.Parse(xml ?? string.Empty)); }
        catch (ConfigFormatException) { return YoloTaskType.Detect; }
    }

    /// <summary>返回任务对应的图标与本地化键，供各页面共用。</summary>
    public static (string Icon, string Key) Info(YoloTaskType task) => task switch
    {
        YoloTaskType.Segment => ("bi-vector-pen", "YoloSegment"),
        YoloTaskType.Classify => ("bi-tags", "YoloClassify"),
        YoloTaskType.Pose => ("bi-dot", "YoloPose"),
        YoloTaskType.Obb => ("bi-record-circle", "YoloObb"),
        _ => ("bi-bounding-box", "YoloDetect"),
    };
}
