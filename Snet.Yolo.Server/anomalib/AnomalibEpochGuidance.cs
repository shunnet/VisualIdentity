namespace Snet.Yolo.Server.anomalib;

/// <summary>
/// EfficientAD 的训练轮数建议：按目标训练步数折算，而不是固定轮数。
/// EfficientAD 每步只处理一张图（批大小 1）；论文与 Anomalib 默认训练 70000 步，实测 12000–27000 步已经足够，推荐约 30000 步，
/// 最后 5% 的步数把学习率降到十分之一；训练图越少，需要的轮数越多。轮数必须是真正要训练的轮数，学习率衰减才会发生。
/// </summary>
/// <param name="Recommended">推荐轮数（100 的倍数，与页面滑块的步长一致）。</param>
/// <param name="TrainingImages">真正参与拟合的图片数（扣掉校准图，再扣掉合成异常验证集）。</param>
/// <param name="Steps">按推荐轮数训练的总步数（一步一张图）。</param>
public sealed record AnomalibEpochGuidance(int Recommended, int TrainingImages, int Steps)
{
    /// <summary>给定轮数训练的总步数。</summary>
    /// <param name="epochs">训练轮数。</param>
    public int StepsFor(int epochs) => checked(epochs * TrainingImages);

    /// <summary>给定轮数是否明显低于建议：不到推荐值的一半，模型通常没训练够。</summary>
    /// <param name="epochs">训练轮数。</param>
    public bool IsFarBelowRecommended(int epochs) => epochs * 2 < Recommended;
}
