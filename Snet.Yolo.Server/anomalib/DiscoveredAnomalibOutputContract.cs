namespace Snet.Yolo.Server.anomalib;

/// <summary>
/// 已确认符合 Anomalib 语义的 ONNX 输出契约。
/// </summary>
public sealed class DiscoveredAnomalibOutputContract
{
    /// <summary>获取整图异常分数张量。</summary>
    public required OnnxTensorDescriptor PredictionScore { get; init; }

    /// <summary>获取整图异常标签张量。</summary>
    public required OnnxTensorDescriptor PredictionLabel { get; init; }

    /// <summary>获取像素级异常图张量。</summary>
    public required OnnxTensorDescriptor AnomalyMap { get; init; }

    /// <summary>获取像素级二值掩码张量。</summary>
    public required OnnxTensorDescriptor PredictionMask { get; init; }
}
