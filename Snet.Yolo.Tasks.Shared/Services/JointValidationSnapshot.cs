using Snet.Yolo.Server.anomalib;

namespace Snet.Yolo.Tasks.Services;

/// <summary>联合验证页面可恢复的用户状态，不包含运行中的任务或模型会话。</summary>
public sealed record JointValidationSnapshot(
    string AnomalibModelKey, string YoloModelIndex,
    IReadOnlyList<JointValidationImage> Images, string? SelectedImageId,
    JointValidationMode Mode, IReadOnlyList<string> Logs,
    float PixelThreshold, int MinimumArea, IReadOnlyDictionary<string, double> YoloParameters);
