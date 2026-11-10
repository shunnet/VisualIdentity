namespace Snet.Yolo.Tasks.Core.Training;

using Snet.Yolo.Tasks.Core.History;

/// <summary>Ultralytics <c>results.csv</c> 的完整解析结果：最佳轮次指标、末轮损失与逐轮曲线。</summary>
public sealed class YoloResultsSeries
{
    /// <summary>csv 中有效的轮数。</summary>
    public int Epochs { get; init; }

    /// <summary>fitness 最高的轮次（与 Ultralytics 选择 best.pt 的轮次一致）。</summary>
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

    /// <summary>末轮训练框损失。</summary>
    public double? BoxLoss { get; init; }

    /// <summary>末轮训练分类损失。</summary>
    public double? ClsLoss { get; init; }

    /// <summary>末轮训练分布焦点损失。</summary>
    public double? DflLoss { get; init; }

    /// <summary>逐轮曲线：Score 为 mAP50-95（分类为 top1 准确率），Loss 为各训练损失之和。</summary>
    public IReadOnlyList<TrainingCurvePoint> Curve { get; init; } = [];

    /// <summary>用于排序和选出最佳模型的主指标：检测类任务为 mAP50-95，分类任务为 top1 准确率。</summary>
    public double? PrimaryScore => Map5095 ?? Accuracy;

    /// <summary>主指标名称，与 <see cref="PrimaryScore"/> 对应。</summary>
    public string PrimaryMetric => Map5095 is not null ? "mAP50-95" : Accuracy is not null ? "accuracy" : string.Empty;
}
