using System.Text.Json.Serialization;

namespace Snet.Yolo.Server.anomalib;

/// <summary>
/// 输入像素值范围。
/// </summary>
public enum AnomalibValueRange
{
    /// <summary>像素值范围为 0 到 1。</summary>
    ZeroToOne,

    /// <summary>像素值范围为 0 到 255。</summary>
    ZeroTo255
}
