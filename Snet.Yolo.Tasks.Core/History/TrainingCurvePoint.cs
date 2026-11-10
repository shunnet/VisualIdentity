namespace Snet.Yolo.Tasks.Core.History;

/// <summary>训练曲线上的一个轮次，用于训练指标页的迷你折线图。</summary>
public sealed class TrainingCurvePoint
{
    /// <summary>从 1 开始的轮次。</summary>
    public int Epoch { get; init; }

    /// <summary>该轮的主指标：YOLO 为 mAP50-95（分类为 top1 准确率），EfficientAD 为验证像素 AUROC；缺失时为空。</summary>
    public double? Score { get; init; }

    /// <summary>该轮的训练损失；缺失时为空。</summary>
    public double? Loss { get; init; }
}
