namespace Snet.Yolo.Tasks.Core.Training;

/// <summary>一次进度更新。</summary>
public sealed record TrainingProgressUpdate(int? Epoch, int? TotalEpochs, double? BoxLoss, double? ClsLoss, double? DflLoss, double? Precision, double? Recall, double? Map50 = null, double? Map5095 = null)
{
    /// <summary>Gets the rounded epoch completion percentage, or zero when progress is unavailable.</summary>
    public int Percent => TotalEpochs is > 0 && Epoch is not null ? (int)Math.Round(Epoch.Value * 100.0 / TotalEpochs.Value) : 0;
}
