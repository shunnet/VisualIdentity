namespace Snet.Yolo.Tasks.Core.History;

/// <summary>一次 YOLO 训练特有的指标，来自 Ultralytics 输出的 results.csv 与训练配置。</summary>
public sealed class YoloRunDetails
{
    /// <summary>所用的预训练基础模型，如 <c>yolo26n.pt</c>。</summary>
    public string ModelName { get; init; } = string.Empty;

    /// <summary>Ultralytics 版本；未探测到时为空。</summary>
    public string YoloVersion { get; init; } = string.Empty;

    /// <summary>训练时是否划分了验证集。</summary>
    public bool UsedValidationSet { get; init; }

    /// <summary>训练集标注实例总数；分类任务为 0。</summary>
    public int Instances { get; init; }

    /// <summary>最佳轮次（按 Ultralytics 的 fitness 选出 best.pt 的那一轮）。</summary>
    public int BestEpoch { get; init; }

    /// <summary>最佳轮次的 mAP50；分类任务为空。</summary>
    public double? Map50 { get; init; }

    /// <summary>最佳轮次的 mAP50-95；分类任务为空。</summary>
    public double? Map5095 { get; init; }

    /// <summary>最佳轮次的精确率；分类任务为空。</summary>
    public double? Precision { get; init; }

    /// <summary>最佳轮次的召回率；分类任务为空。</summary>
    public double? Recall { get; init; }

    /// <summary>分类任务最佳轮次的 top1 准确率；其他任务为空。</summary>
    public double? Accuracy { get; init; }

    /// <summary>最后一轮的框损失。</summary>
    public double? BoxLoss { get; init; }

    /// <summary>最后一轮的分类损失。</summary>
    public double? ClsLoss { get; init; }

    /// <summary>最后一轮的分布焦点损失。</summary>
    public double? DflLoss { get; init; }

    /// <summary>按轮次排列的曲线点。</summary>
    public List<TrainingCurvePoint> Curve { get; init; } = [];
}
