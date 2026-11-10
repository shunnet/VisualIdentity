using System.Text.Json.Serialization;

namespace Snet.Yolo.Server.anomalib;

/// <summary>
/// 异常阈值来源。
/// </summary>
public enum AnomalibThresholdSource
{
    /// <summary>使用正常样本与合成异常完成阈值校准。</summary>
    SyntheticCalibration,

    /// <summary>使用真实异常验证集完成阈值校准。</summary>
    ValidationDataset,

    /// <summary>由用户手动指定阈值。</summary>
    Manual,
    /// <summary>使用训练内部独立留出的正常图片标定阈值。</summary>
    NormalCalibration
}
