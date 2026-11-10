using System.Text.Json.Serialization;

namespace Snet.Yolo.Server.anomalib;

/// <summary>
/// 图像颜色空间。
/// </summary>
public enum AnomalibColorSpace
{
    /// <summary>红、绿、蓝通道顺序。</summary>
    Rgb,

    /// <summary>蓝、绿、红通道顺序。</summary>
    Bgr
}
