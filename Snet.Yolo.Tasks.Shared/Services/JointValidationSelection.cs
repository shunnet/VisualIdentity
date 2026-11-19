using Snet.Yolo.Server.anomalib;

namespace Snet.Yolo.Tasks.Services;

/// <summary>联合验证重启后恢复的模型选择和识别模式。</summary>
public sealed record JointValidationSelection(string AnomalibKey, string YoloIndex, JointValidationMode Mode);
