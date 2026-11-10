using System.Text.Json.Serialization;

namespace Snet.Yolo.Server.anomalib;

/// <summary>
/// Anomalib 模型输入契约。
/// </summary>
public sealed class AnomalibInputContract
{
    /// <summary>获取或设置 ONNX 输入节点名称。</summary>
    [JsonRequired]
    public string Name { get; set; } = string.Empty;

    /// <summary>获取或设置输入张量元素类型。</summary>
    [JsonRequired]
    public OnnxTensorElementType ElementType { get; set; } = OnnxTensorElementType.Float32;

    /// <summary>获取或设置输入张量布局。</summary>
    [JsonRequired]
    public AnomalibTensorLayout Layout { get; set; } = AnomalibTensorLayout.Nchw;

    /// <summary>获取或设置输入图像颜色空间。</summary>
    [JsonRequired]
    public AnomalibColorSpace ColorSpace { get; set; } = AnomalibColorSpace.Rgb;

    /// <summary>获取或设置模型输入宽度。</summary>
    [JsonRequired]
    public int Width { get; set; }

    /// <summary>获取或设置模型输入高度。</summary>
    [JsonRequired]
    public int Height { get; set; }

    /// <summary>获取或设置图像缩放方式。</summary>
    [JsonRequired]
    public AnomalibResizeMode ResizeMode { get; set; }

    /// <summary>获取或设置插值约定；旧清单省略时保留原双线性采样行为。</summary>
    public AnomalibInterpolation Interpolation { get; set; }

    /// <summary>获取或设置送入模型前的像素值范围。</summary>
    [JsonRequired]
    public AnomalibValueRange ValueRange { get; set; }

    /// <summary>获取或设置 ONNX 图中是否已内嵌通道归一化。</summary>
    [JsonRequired]
    public bool NormalizationEmbedded { get; set; }

    /// <summary>获取或设置未内嵌归一化时使用的 RGB 通道均值。</summary>
    public float[]? Mean { get; set; }

    /// <summary>获取或设置未内嵌归一化时使用的 RGB 通道标准差。</summary>
    public float[]? Std { get; set; }
}
