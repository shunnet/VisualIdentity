using System.Text.Json.Serialization;

namespace Snet.Yolo.Server.anomalib;

/// <summary>
/// 图像缩放方式。
/// </summary>
public enum AnomalibResizeMode
{
    /// <summary>直接拉伸到模型输入尺寸。</summary>
    Stretch,

    /// <summary>保持宽高比并在空白区域填充。</summary>
    Letterbox,

    /// <summary>保持宽高比放大后从中心裁剪。</summary>
    CenterCrop
}
