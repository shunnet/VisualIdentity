using System.Text.Json.Serialization;

namespace Snet.Yolo.Server.anomalib;

/// <summary>
/// 图像张量布局。
/// </summary>
public enum AnomalibTensorLayout
{
    /// <summary>批次、通道、高度、宽度布局。</summary>
    Nchw,

    /// <summary>批次、高度、宽度、通道布局。</summary>
    Nhwc
}
