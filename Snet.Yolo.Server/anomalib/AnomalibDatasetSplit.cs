namespace Snet.Yolo.Server.anomalib;

using System.Text.Json;
using System.Text.Json.Serialization;

/// <summary>正常图片的稳定训练集与校准集划分。</summary>
/// <param name="TrainingImages">训练图片。</param>
/// <param name="CalibrationImages">校准与一致性门禁图片。</param>
/// <param name="DatasetSha256">由有序内容摘要计算的数据集摘要。</param>
public sealed record AnomalibDatasetSplit(
    IReadOnlyList<AnomalibImageSource> TrainingImages,
    IReadOnlyList<AnomalibImageSource> CalibrationImages,
    string DatasetSha256)
{
    /// <summary>去重后的正常图片总数。</summary>
    public int TotalUniqueImages => TrainingImages.Count + CalibrationImages.Count;
}
