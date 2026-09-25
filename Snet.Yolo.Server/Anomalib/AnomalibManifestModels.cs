using System.Text.Json.Serialization;

namespace Snet.Yolo.Server.Anomalib;

/// <summary>
/// Anomalib 模型算法。
/// </summary>
public enum AnomalibAlgorithm
{
    /// <summary>PaDiM 异常检测算法。</summary>
    Padim,

    /// <summary>EfficientAD Small 异常检测算法。</summary>
    EfficientAdSmall,

    /// <summary>PatchCore 实验算法。</summary>
    PatchCore
}

/// <summary>
/// ONNX 张量元素类型。
/// </summary>
public enum OnnxTensorElementType
{
    /// <summary>32 位浮点数。</summary>
    Float32,

    /// <summary>64 位浮点数。</summary>
    Float64,

    /// <summary>64 位有符号整数。</summary>
    Int64,

    /// <summary>8 位无符号整数。</summary>
    UInt8,

    /// <summary>布尔值。</summary>
    Boolean
}

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
    Manual
}

/// <summary>
/// Anomalib 模型部署清单。
/// </summary>
public sealed class AnomalibModelManifest
{
    /// <summary>获取或设置清单架构版本。</summary>
    [JsonRequired]
    public string SchemaVersion { get; set; } = string.Empty;

    /// <summary>获取或设置模型家族，固定为 anomalib。</summary>
    [JsonRequired]
    public string Family { get; set; } = string.Empty;

    /// <summary>获取或设置生成模型所用的 Anomalib 版本。</summary>
    [JsonRequired]
    public string AnomalibVersion { get; set; } = string.Empty;

    /// <summary>获取或设置异常检测算法。</summary>
    [JsonRequired]
    public AnomalibAlgorithm Algorithm { get; set; }

    /// <summary>获取或设置 ONNX 模型文件的 SHA-256 摘要。</summary>
    [JsonRequired]
    public string ModelSha256 { get; set; } = string.Empty;

    /// <summary>获取或设置模型输入契约。</summary>
    [JsonRequired]
    public AnomalibInputContract Input { get; set; } = new();

    /// <summary>获取或设置模型输出节点契约。</summary>
    [JsonRequired]
    public AnomalibOutputNames Outputs { get; set; } = new();

    /// <summary>获取或设置后处理契约。</summary>
    [JsonRequired]
    public AnomalibPostProcessingContract PostProcessing { get; set; } = new();
}

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

/// <summary>
/// Anomalib ONNX 输出节点名称。
/// </summary>
public sealed class AnomalibOutputNames
{
    /// <summary>获取或设置整图异常分数输出名称。</summary>
    [JsonRequired]
    public string PredictionScore { get; set; } = string.Empty;

    /// <summary>获取或设置整图异常标签输出名称。</summary>
    [JsonRequired]
    public string PredictionLabel { get; set; } = string.Empty;

    /// <summary>获取或设置像素级异常图输出名称。</summary>
    [JsonRequired]
    public string AnomalyMap { get; set; } = string.Empty;

    /// <summary>获取或设置像素级二值掩码输出名称。</summary>
    [JsonRequired]
    public string PredictionMask { get; set; } = string.Empty;
}

/// <summary>
/// Anomalib 后处理契约。
/// </summary>
public sealed class AnomalibPostProcessingContract
{
    /// <summary>获取或设置判断异常的阈值。</summary>
    [JsonRequired]
    public float Threshold { get; set; }

    /// <summary>获取或设置异常阈值来源。</summary>
    [JsonRequired]
    public AnomalibThresholdSource ThresholdSource { get; set; }
}

/// <summary>
/// Anomalib 清单无效时抛出的异常。
/// </summary>
public sealed class AnomalibManifestException : Exception
{
    /// <summary>
    /// 使用错误消息创建清单异常。
    /// </summary>
    /// <param name="message">中文错误消息。</param>
    public AnomalibManifestException(string message) : base(message)
    {
    }

    /// <summary>
    /// 使用错误消息和内部异常创建清单异常。
    /// </summary>
    /// <param name="message">中文错误消息。</param>
    /// <param name="innerException">原始异常。</param>
    public AnomalibManifestException(string message, Exception innerException) : base(message, innerException)
    {
    }
}
