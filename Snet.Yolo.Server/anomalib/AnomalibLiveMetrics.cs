namespace Snet.Yolo.Server.anomalib;

/// <summary>
/// Anomalib 训练过程中的实时指标：由 Python 流水线输出的结构化进度行解析而来，
/// 训练页右侧据此显示与 YOLO 一致的“训练指标”卡片。
/// </summary>
public sealed class AnomalibLiveMetrics
{
    /// <summary>曲线最多保留的轮次点数，超出后丢弃最早的点，避免状态快照随训练变大。</summary>
    public const int MaximumHistoryPoints = 2000;

    /// <summary>当前阶段：resize、train、extract、coreset 等；尚未收到进度时为空。</summary>
    public string Stage { get; set; } = string.Empty;

    /// <summary>当前阶段已完成的步数。</summary>
    public int StageCurrent { get; set; }

    /// <summary>当前阶段的总步数；未知时为 0。</summary>
    public int StageTotal { get; set; }

    /// <summary>EfficientAD 已完成的轮数；其他算法为 0。</summary>
    public int Epoch { get; set; }

    /// <summary>EfficientAD 的最大轮数；其他算法为 0。</summary>
    public int TotalEpochs { get; set; }

    /// <summary>最近一轮的总损失。</summary>
    public double? TrainLoss { get; set; }

    /// <summary>最近一轮的学生网络损失。</summary>
    public double? StudentLoss { get; set; }

    /// <summary>最近一轮的自编码器损失。</summary>
    public double? AutoencoderLoss { get; set; }

    /// <summary>最近一轮验证集像素 AUROC（合成异常，仅反映训练是否停滞）。</summary>
    public double? PixelAuroc { get; set; }

    /// <summary>到目前为止最好的验证像素 AUROC。</summary>
    public double? BestPixelAuroc { get; set; }

    /// <summary>最好验证指标出现的轮次。</summary>
    public int BestEpoch { get; set; }

    /// <summary>PatchCore 的训练 patch 总数。</summary>
    public long PatchCount { get; set; }

    /// <summary>PatchCore 实际保留参与 coreset 的 patch 比例（显存不足时小于 1）。</summary>
    public double? KeepFraction { get; set; }

    /// <summary>PatchCore 记忆库大小。</summary>
    public int MemoryBank { get; set; }

    /// <summary>PatchCore 全部 patch 嵌入的预估显存（MiB）。</summary>
    public long EmbeddingMiB { get; set; }

    /// <summary>训练开始时 GPU 的可用显存（MiB）。</summary>
    public long? FreeVramMiB { get; set; }

    /// <summary>正常图标定得到的整图原始阈值。</summary>
    public double? ImageThreshold { get; set; }

    /// <summary>内部正常图的最高整图原始分数。</summary>
    public double? NormalScoreMax { get; set; }

    /// <summary>各轮次曲线点，按轮次升序。</summary>
    public List<AnomalibEpochPoint> History { get; set; } = [];

    /// <summary>是否已收到任何可显示的指标。</summary>
    public bool HasValue => Epoch > 0 || StageTotal > 0 || MemoryBank > 0 || ImageThreshold is not null;

    /// <summary>创建与可变源对象无关的副本。</summary>
    /// <returns>深拷贝，曲线点为不可变对象，因此只复制列表。</returns>
    public AnomalibLiveMetrics Clone() => new()
    {
        Stage = Stage,
        StageCurrent = StageCurrent,
        StageTotal = StageTotal,
        Epoch = Epoch,
        TotalEpochs = TotalEpochs,
        TrainLoss = TrainLoss,
        StudentLoss = StudentLoss,
        AutoencoderLoss = AutoencoderLoss,
        PixelAuroc = PixelAuroc,
        BestPixelAuroc = BestPixelAuroc,
        BestEpoch = BestEpoch,
        PatchCount = PatchCount,
        KeepFraction = KeepFraction,
        MemoryBank = MemoryBank,
        EmbeddingMiB = EmbeddingMiB,
        FreeVramMiB = FreeVramMiB,
        ImageThreshold = ImageThreshold,
        NormalScoreMax = NormalScoreMax,
        History = [.. History],
    };
}
