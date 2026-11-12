namespace Snet.Yolo.Server.anomalib;

using System.Text.Json;
using System.Text.Json.Serialization;

/// <summary>受支持模型的集中映射，避免页面、服务与 Python 脚本各自维护名称。</summary>
public static class AnomalibModelCatalog
{
    /// <summary>获取指定模型的不可变描述。</summary>
    /// <param name="kind">模型类型。</param>
    /// <returns>模型描述。</returns>
    public static AnomalibModelDescriptor Get(AnomalibModelKind kind) => kind switch
    {
        AnomalibModelKind.Padim => new(kind, "padim", "PaDiM", false),
        AnomalibModelKind.EfficientAdSmall => new(kind, "efficient_ad_small", "EfficientAD Small", false),
        AnomalibModelKind.PatchCore => new(AnomalibModelKind.PatchCore, "patchcore", "PatchCore", false),
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "不支持的 Anomalib 模型。"),
    };

    /// <summary>获取指定模型的输入尺寸建议（训练页据此给出推荐值并在明显偏大时提示）。</summary>
    /// <param name="kind">模型类型。</param>
    /// <returns>推荐尺寸与建议上限。</returns>
    public static AnomalibSizeGuidance GetSizeGuidance(AnomalibModelKind kind) => kind switch
    {
        // PaDiM：实测 256 px 最好，越大越差，显存按边长平方增长。
        AnomalibModelKind.Padim => new(256, 384),
        // EfficientAD：网络结构按 256 px 设计，更大尺寸训练更慢，没有证据表明更准。
        AnomalibModelKind.EfficientAdSmall => new(256, 512),
        // PatchCore：384–512 px 区分能力最好且最划算，640 px 起不再提升。
        AnomalibModelKind.PatchCore => new(512, 768),
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "不支持的 Anomalib 模型。"),
    };

    /// <summary>
    /// 训练页应该给出的输入尺寸：上一次训练用的就是这个模型、且尺寸没有超过它的建议上限时，沿用那次的尺寸（视为用户自己的选择），
    /// 否则用该模型的推荐尺寸——不同模型的合理尺寸差几倍，把 PatchCore 的 768 带给 EfficientAD 会让它远离设计范围
    /// （同一批真实异常图上，768 px 的 EfficientAD 检出 54%，256 px 的检出 71%）；超过上限的尺寸也不再沿用，需要时在页面上手动拖。
    /// </summary>
    /// <param name="kind">要训练的模型。</param>
    /// <param name="previousKind">上一次训练的模型；没有训练过为 null。</param>
    /// <param name="previousImageSize">上一次训练的输入尺寸。</param>
    public static int ResolveImageSize(AnomalibModelKind kind, AnomalibModelKind? previousKind, int previousImageSize)
    {
        var guidance = GetSizeGuidance(kind);
        return previousKind == kind && previousImageSize >= 128 && !guidance.IsAboveRecommendedMaximum(previousImageSize)
            ? previousImageSize
            : guidance.Recommended;
    }

    /// <summary>EfficientAD 论文与 Anomalib 默认的训练步数（批大小 1，一步一张图）。</summary>
    public const int EfficientAdOfficialSteps = 70_000;

    /// <summary>
    /// EfficientAD 的推荐训练步数。官方是 <see cref="EfficientAdOfficialSteps"/>，但同一批真实异常图上（256 px）早停于约 12000 步的旧配方、
    /// 固定 27000 步的新配方检出率没有差别（72.7% 对 74.4%，AUROC 0.940 对 0.944），所以取 30000 步：约为官方的 43%，训练时间也大致减半。
    /// </summary>
    public const int EfficientAdTargetSteps = 30_000;

    /// <summary>流水线从训练图里留出做合成异常验证（同时估计归一化分位数）的比例，与 anomalib_pipeline.py 的 test_split_ratio 一致。</summary>
    public const double ValidationRatio = 0.2;

    /// <summary>
    /// 获取迭代训练模型（目前只有 EfficientAD）的训练轮数建议；PaDiM 与 PatchCore 一遍拟合，没有轮数可调，返回 null。
    /// </summary>
    /// <param name="kind">模型类型。</param>
    /// <param name="normalImageCount">工程里的正常图片数。</param>
    /// <param name="calibrationRatio">划入校准集的比例（<see cref="AnomalibTrainingOptions.CalibrationRatio"/>）。</param>
    public static AnomalibEpochGuidance? GetEpochGuidance(AnomalibModelKind kind, int normalImageCount, double calibrationRatio = 0.1)
    {
        if (kind != AnomalibModelKind.EfficientAdSmall || normalImageCount < 2) { return null; }
        // 与 AnomalibDatasetPlanner、流水线里的划分一致：先稳定划出校准图，剩下的再留出 20% 做验证。
        var calibration = Math.Clamp((int)Math.Round(normalImageCount * calibrationRatio, MidpointRounding.AwayFromZero), 1, normalImageCount - 1);
        var training = normalImageCount - calibration;
        var fitting = Math.Max(1, training - (int)Math.Ceiling(training * ValidationRatio));
        var epochs = Math.Clamp((int)Math.Round((double)EfficientAdTargetSteps / fitting / 100d) * 100, 100, 10_000);
        return new AnomalibEpochGuidance(epochs, fitting, epochs * fitting);
    }
}
