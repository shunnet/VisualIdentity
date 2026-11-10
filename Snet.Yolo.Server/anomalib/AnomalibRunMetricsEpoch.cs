namespace Snet.Yolo.Server.anomalib;

using System.Text.Json.Serialization;

/// <summary>Python 流水线写入 <c>metrics.json</c> 的单个 EfficientAD 轮次。</summary>
public sealed class AnomalibRunMetricsEpoch
{
    /// <summary>从 1 开始的轮次。</summary>
    [JsonPropertyName("epoch")] public int Epoch { get; init; }

    /// <summary>本轮总损失。</summary>
    [JsonPropertyName("trainLoss")] public double? TrainLoss { get; init; }

    /// <summary>本轮验证像素 AUROC。</summary>
    [JsonPropertyName("pixelAuroc")] public double? PixelAuroc { get; init; }
}
