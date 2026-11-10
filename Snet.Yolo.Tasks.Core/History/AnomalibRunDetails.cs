namespace Snet.Yolo.Tasks.Core.History;

using System.Text.Json.Serialization;

/// <summary>一次 Anomalib 训练特有的指标：校准验证、阈值、显存与轮次曲线。训练成败都会保存，便于留档对比。</summary>
public sealed class AnomalibRunDetails
{
    /// <summary>参与训练的正常图片数。</summary>
    public int TrainingImages { get; init; }

    /// <summary>独立校准集（最终门禁）图片数。</summary>
    public int CalibrationImages { get; init; }

    /// <summary>校准门禁是否通过。</summary>
    public bool GatePassed { get; init; }

    /// <summary>ONNX 模型与清单是否已完整导出。</summary>
    public bool ExportSucceeded { get; init; }

    /// <summary>模型是否已自动注册到 Anomalib 验证页。</summary>
    public bool Registered { get; init; }

    /// <summary>计划比较的校准图片总数。</summary>
    public int TotalSamples { get; init; }

    /// <summary>逐图通过的校准图片数。</summary>
    public int PassedSamples { get; init; }

    /// <summary>完成双路推理比较的正常图片数。</summary>
    public int NormalSamples { get; init; }

    /// <summary>被判为异常的正常图片数（误报）。</summary>
    public int FalsePositives { get; init; }

    /// <summary>训练模型与 ONNX 的最大分数差。</summary>
    public double? MaxScoreDifference { get; init; }

    /// <summary>训练模型与 ONNX 的最小掩码 IoU。</summary>
    public double? MinimumMaskIou { get; init; }

    /// <summary>训练模型与 ONNX 判定不一致的图片数。</summary>
    public int LabelMismatches { get; init; }

    /// <summary>正常图标定得到的整图原始阈值。</summary>
    public double? ImageThreshold { get; init; }

    /// <summary>内部正常图的最高整图原始分数。</summary>
    public double? NormalScoreMax { get; init; }

    /// <summary>内部正常图整图分数的均值。</summary>
    public double? NormalScoreMean { get; init; }

    /// <summary>参与阈值标定的内部正常图数量。</summary>
    public int ThresholdSamples { get; init; }

    /// <summary>EfficientAD 实际完成的轮数；其他算法为 0。</summary>
    public int CompletedEpochs { get; init; }

    /// <summary>EfficientAD 最佳模型所在轮次；其他算法为 0。</summary>
    public int BestEpoch { get; init; }

    /// <summary>EfficientAD 最佳验证像素 AUROC（合成异常，仅反映训练是否停滞）。</summary>
    public double? BestPixelAuroc { get; init; }

    /// <summary>训练期间 GPU 显存峰值（MiB）；CPU 训练或未采集时为空。</summary>
    public long? PeakVramMiB { get; init; }

    /// <summary>PatchCore 记忆库大小；其他算法为 0。</summary>
    public int MemoryBank { get; init; }

    /// <summary>PatchCore 因显存预算保留的 patch 比例；其他算法为空。</summary>
    public double? KeepFraction { get; init; }

    /// <summary>各阶段耗时（秒）：resize、train、calibration、export、parity。</summary>
    public Dictionary<string, double> StageSeconds { get; init; } = [];

    /// <summary>EfficientAD 的轮次曲线；其他算法为空。</summary>
    public List<TrainingCurvePoint> Curve { get; init; } = [];

    /// <summary>逐图通过率，取值 0 至 1；没有校准样本时为空。</summary>
    [JsonIgnore]
    public double? PassRate => TotalSamples > 0 ? (double)PassedSamples / TotalSamples : null;

    /// <summary>正常图误报率，取值 0 至 1；没有完成校准的正常图时为空。</summary>
    [JsonIgnore]
    public double? FalsePositiveRate => NormalSamples > 0 ? (double)FalsePositives / NormalSamples : null;
}
