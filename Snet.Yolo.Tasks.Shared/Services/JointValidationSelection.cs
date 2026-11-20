using Snet.Yolo.Server.anomalib;

namespace Snet.Yolo.Tasks.Services;

/// <summary>联合验证重启后恢复的模型选择、识别模式及设置页签。</summary>
/// <param name="AnomalibKey">选中的异常检测模型。</param>
/// <param name="YoloIndex">选中的 YOLO 模型。</param>
/// <param name="Mode">最近一次执行的识别模式。</param>
/// <param name="ShowAnomalibSettings">是否显示 Anomalib 设置，旧版偏好默认显示 YOLO。</param>
public sealed record JointValidationSelection(string AnomalibKey, string YoloIndex, JointValidationMode Mode, bool ShowAnomalibSettings = false);
