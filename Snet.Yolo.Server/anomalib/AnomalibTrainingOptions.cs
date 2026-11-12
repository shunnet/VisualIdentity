namespace Snet.Yolo.Server.anomalib;

using System.Text.Json;
using System.Text.Json.Serialization;

/// <summary>Anomalib 训练选项。</summary>
public sealed class AnomalibTrainingOptions
{
    /// <summary>校准集正常图片允许的最高误报比例。</summary>
    public const double MaximumNormalFalsePositiveRate = 0.05;

    /// <summary>要训练的异常检测模型。</summary>
    public AnomalibModelKind Model { get; set; } = AnomalibModelKind.Padim;

    /// <summary>正方形模型输入尺寸，必须在 128 至 2048 之间且为 32 的倍数。</summary>
    public int ImageSize { get; set; } = 640;

    /// <summary>迭代训练模型的训练轮数（没有启用早停时就是实际训练的轮数）；PaDiM 与 PatchCore 一遍拟合，不使用它。</summary>
    public int MaxEpochs { get; set; } = 500;

    /// <summary>
    /// EfficientAD 是否在验证指标持续无改善时提前结束；其他模型忽略此设置。默认关闭：验证指标来自合成异常，
    /// 与真实缺陷的效果几乎不相关，早停容易在模型还没训练好时就停下；固定轮数训练并采用最终权重才是论文的做法。
    /// </summary>
    public bool EarlyStoppingEnabled { get; set; }

    /// <summary>EfficientAD 连续无明显改善的验证轮数，范围 1 至 1000。</summary>
    public int EarlyStoppingPatience { get; set; } = 50;

    /// <summary>像素级 AUROC 认定改善所需的最小增量，范围 0 至 1。</summary>
    public double EarlyStoppingMinDelta { get; set; } = 0.001;

    /// <summary>设备选择：auto、cpu 或 cuda。</summary>
    public string Device { get; set; } = "auto";

    /// <summary>从正常图片中稳定划入校准集的比例。</summary>
    public double CalibrationRatio { get; set; } = 0.1;

    /// <summary>保证数据划分和训练可复现的随机种子。</summary>
    public int RandomSeed { get; set; } = 42;

    /// <summary>训练数据加载进程数；零表示由 Python 在当前平台安全选择。</summary>
    public int WorkerCount { get; set; }

    /// <summary>验证训练选项并在无效时抛出可执行的中文异常。</summary>
    public void Validate()
    {
        _ = AnomalibModelCatalog.Get(Model);
        if (ImageSize is < 128 or > 2048 || ImageSize % 32 != 0)
        {
            throw new ArgumentOutOfRangeException(nameof(ImageSize), "输入尺寸必须在 128 至 2048 之间且为 32 的倍数。");
        }
        if (CalibrationRatio is < 0.05 or > 0.5)
        {
            throw new ArgumentOutOfRangeException(nameof(CalibrationRatio), "正常图片校准比例必须在 5% 至 50% 之间。");
        }
        if (MaxEpochs is < 1 or > 10_000)
        {
            throw new ArgumentOutOfRangeException(nameof(MaxEpochs), "训练轮数必须在 1 至 10000 之间。");
        }
        if (WorkerCount is < 0 or > 64)
        {
            throw new ArgumentOutOfRangeException(nameof(WorkerCount), "数据加载进程数必须在 0 至 64 之间。");
        }
        if (!double.IsFinite(EarlyStoppingMinDelta))
        {
            throw new ArgumentOutOfRangeException(nameof(EarlyStoppingMinDelta), "早停最小改善幅度必须是有限值。");
        }
        if (Model == AnomalibModelKind.EfficientAdSmall && EarlyStoppingEnabled)
        {
            if (EarlyStoppingPatience is < 1 or > 1000)
            {
                throw new ArgumentOutOfRangeException(nameof(EarlyStoppingPatience), "早停耐心轮数必须在 1 至 1000 之间。");
            }
            if (EarlyStoppingMinDelta is < 0 or > 1)
            {
                throw new ArgumentOutOfRangeException(nameof(EarlyStoppingMinDelta), "早停最小改善幅度必须是 0 至 1 之间的有限值。");
            }
        }
        var selectedGpus = Snet.Yolo.Server.models.GpuDeviceSelection.Parse(Device, anomalib: true);
        if (Model != AnomalibModelKind.EfficientAdSmall && selectedGpus.Length > 1)
        {
            throw new ArgumentException("当前模型仅支持单张 GPU 训练。", nameof(Device));
        }
    }
}
