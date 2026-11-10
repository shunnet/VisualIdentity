namespace Snet.Yolo.Server.anomalib;

/// <summary>EfficientAD 单个训练轮次的实时指标，用于训练页曲线和训练记录档案。</summary>
public sealed class AnomalibEpochPoint
{
    /// <summary>从 1 开始的轮次。</summary>
    public int Epoch { get; init; }

    /// <summary>本轮总损失；Lightning 未报告时为空。</summary>
    public double? TrainLoss { get; init; }

    /// <summary>本轮结束后验证集上的像素级 AUROC（合成异常），仅用于判断训练是否停滞。</summary>
    public double? PixelAuroc { get; init; }
}
