using System.Text.Json.Serialization;

namespace Snet.Yolo.Server.anomalib;

/// <summary>
/// Anomalib 模型算法。
/// </summary>
public enum AnomalibAlgorithm
{
    /// <summary>PaDiM 异常检测算法。</summary>
    Padim,

    /// <summary>EfficientAD Small 异常检测算法。</summary>
    EfficientAdSmall,

    /// <summary>PatchCore 异常检测算法。</summary>
    PatchCore
}
