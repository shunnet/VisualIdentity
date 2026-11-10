namespace Snet.Yolo.Tasks.Core.Training;

/// <summary>一次训练的结果摘要（来自 Ultralytics 输出的 results.csv）。</summary>
/// <param name="Epochs">已完成的轮数。</param>
/// <param name="Map50">各轮中最好的 mAP50。</param>
/// <param name="Map5095">各轮中最好的 mAP50-95。</param>
/// <param name="Precision">最好 mAP50 那一轮的精确率。</param>
/// <param name="Recall">最好 mAP50 那一轮的召回率。</param>
public sealed record TrainingResultSummary(int Epochs, double Map50, double Map5095, double Precision, double Recall);
