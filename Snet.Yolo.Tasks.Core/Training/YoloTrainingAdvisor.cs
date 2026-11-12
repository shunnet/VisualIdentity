using Snet.Yolo.Tasks.Core.Config;

namespace Snet.Yolo.Tasks.Core.Training;

/// <summary>
/// 按数据集画像与显卡给出训练推荐值：输入尺寸、模型规格、训练轮数、是否使用验证集。
///
/// 依据（同一份真实数据上的对比实验，见 docs/plans/2026-11-12-yolo-accuracy.md）：
///   · 输入尺寸看“最小的那一批目标”：最小的 5% 目标在网络输入上的短边要有 <see cref="TargetBoxPixels"/> 像素左右才看得清；
///     输入也不必超过原图分辨率；但也不低于默认的 <see cref="MinImageSize"/>——实验里 416 比 640 低了 0.046 mAP50-95，960 在大目标上没有收益；
///   · 模型规格：2000–3000 张训练图上 n 与 s 的精度相同、m 没有更好，所以几千张以内用最快的 n；显存不够时批大小被迫很小；
///   · 轮数按“总共需要多少次参数更新”折算，而不是固定 500：100 轮（约 3700 次）的最终精度最高，300 轮（约 11000 次）过拟合反而更差；
///   · 有足够多的图片就划分验证集：能看到没见过的图片上的 mAP，长训练过拟合时 best.pt 也能避开后段（验证图不多时 best.pt 的成绩偏乐观）。
/// </summary>
public static class YoloTrainingAdvisor
{
    /// <summary>最小的那批目标在网络输入上的短边目标像素。</summary>
    public const double TargetBoxPixels = 16d;
    /// <summary>用“最小的 5% 目标”衡量小目标。</summary>
    public const double SmallBoxQuantile = 0.05;
    /// <summary>推荐输入尺寸下限（Ultralytics 默认值）。</summary>
    public const int MinImageSize = 640;
    /// <summary>推荐输入尺寸上限：更大的图建议切图后再标注训练。</summary>
    public const int MaxImageSize = 1280;
    /// <summary>图像分类的推荐输入尺寸（Ultralytics 官方默认）。</summary>
    public const int ClassifyImageSize = 224;
    /// <summary>低于此图片数不划分验证集（留出的 10% 只有几十张，mAP 基本是噪声，还让训练图更少）。</summary>
    public const int MinImagesForValidation = 300;
    /// <summary>折算训练轮数用的目标参数更新次数（批大小按 Ultralytics 的名义批大小 64 计）。</summary>
    public const int TargetOptimizerSteps = 4000;
    /// <summary>推荐轮数下限与上限；轮数是 <see cref="EpochStep"/> 的倍数，与弹窗里的滑块步长一致。</summary>
    public const int MinEpochs = 100, MaxEpochs = 500, EpochStep = 100;
    /// <summary>输入尺寸滑块的步长。</summary>
    private const int SizeStep = 32;
    private static readonly string[] Scales = ["n", "s", "m"];

    /// <summary>生成推荐值。</summary>
    /// <param name="profile">数据集画像。</param>
    /// <param name="taskType">YOLO 任务类型。</param>
    /// <param name="hardware">训练设备；未提供时不按硬件限制模型规格。</param>
    public static YoloRecommendation Recommend(YoloDatasetProfile profile, YoloTaskType taskType, YoloHardware hardware = default)
    {
        var reasons = new List<YoloAdviceItem>();
        var imageSize = RecommendImageSize(profile, taskType, reasons);
        var scale = RecommendScale(profile, hardware, reasons);
        var epochs = RecommendEpochs(profile, reasons);
        var useValidation = profile.AnnotatedImageCount >= MinImagesForValidation;
        reasons.Add(useValidation
            ? new YoloAdviceItem(YoloAdviceField.Validation, "YoloAdviceValidationOn", [profile.AnnotatedImageCount])
            : new YoloAdviceItem(YoloAdviceField.Validation, "YoloAdviceValidationOff", [profile.AnnotatedImageCount, MinImagesForValidation]));
        return new YoloRecommendation(imageSize, scale, epochs, useValidation, reasons);
    }

    private static int RecommendImageSize(YoloDatasetProfile profile, YoloTaskType taskType, List<YoloAdviceItem> reasons)
    {
        if (taskType == YoloTaskType.Classify)
        {
            reasons.Add(new YoloAdviceItem(YoloAdviceField.ImageSize, "YoloAdviceSizeClassify", [ClassifyImageSize]));
            return ClassifyImageSize;
        }
        if (profile.Boxes.Count == 0)
        {
            reasons.Add(new YoloAdviceItem(YoloAdviceField.ImageSize, "YoloAdviceSizeNoBoxes", [MinImageSize]));
            return MinImageSize;
        }
        // 框在输入上的像素 = 短边占图片长边的比例 × 输入尺寸：直接算出让最小的 5% 目标达到目标像素所需的输入尺寸，按 32 向上取整。
        var fraction = profile.ShortSideFractionQuantile(SmallBoxQuantile);
        var needed = fraction > 0 ? Math.Ceiling(TargetBoxPixels / fraction / SizeStep) * SizeStep : MaxImageSize;
        var size = (int)Math.Clamp(needed, MinImageSize, MaxImageSize);
        var smallest = fraction * size;
        if (smallest < TargetBoxPixels)
        {
            // 到上限了目标还是偏小：只能提示切图，不能无限加大输入。
            reasons.Add(new YoloAdviceItem(YoloAdviceField.ImageSize, "YoloAdviceSizeStillSmall", [size, Math.Round(smallest, 1), TargetBoxPixels]));
            return size;
        }
        // 输入不必超过原图分辨率：放大一张图不会多出信息。
        if (profile.MedianLongEdge > 0)
        {
            var sourceCap = Math.Max(MinImageSize, (profile.MedianLongEdge + SizeStep - 1) / SizeStep * SizeStep);
            if (size > sourceCap)
            {
                reasons.Add(new YoloAdviceItem(YoloAdviceField.ImageSize, "YoloAdviceSizeSource", [sourceCap, profile.MedianLongEdge]));
                return sourceCap;
            }
        }
        reasons.Add(new YoloAdviceItem(YoloAdviceField.ImageSize, "YoloAdviceSizeObjects", [size, Math.Round(smallest, 1), TargetBoxPixels]));
        return size;
    }

    private static string RecommendScale(YoloDatasetProfile profile, YoloHardware hardware, List<YoloAdviceItem> reasons)
    {
        // 实测（2000–3000 张训练图、同一份真实数据）：n 与 s 的精度相同，m 反而更低；所以几千张以内用最快的 n，
        // 更大的规格要图片多到几千张以上才值得，l / x 只作为用户自己的选择，不推荐。
        var byData = profile.AnnotatedImageCount switch
        {
            < 3000 => "n",
            < 15000 => "s",
            _ => "m",
        };
        // 设备上限：CPU 训练大模型要等很久；显存小于 8 GB 时 m 规格的批大小被压到很小，训练又慢又不稳。
        var cap = hardware switch
        {
            { IsCpu: true } => "n",
            { VramMb: > 0 and < 8000 } => "s",
            _ => "m",
        };
        var scale = Array.IndexOf(Scales, byData) <= Array.IndexOf(Scales, cap) ? byData : cap;
        reasons.Add(scale == byData
            ? new YoloAdviceItem(YoloAdviceField.Model, "YoloAdviceModelData", [scale, profile.AnnotatedImageCount])
            : hardware.IsCpu
                ? new YoloAdviceItem(YoloAdviceField.Model, "YoloAdviceModelCpu", [scale])
                : new YoloAdviceItem(YoloAdviceField.Model, "YoloAdviceModelVram", [scale, Math.Round(hardware.VramMb / 1024d, 1)]));
        return scale;
    }

    private static int RecommendEpochs(YoloDatasetProfile profile, List<YoloAdviceItem> reasons)
    {
        var images = Math.Max(1, profile.AnnotatedImageCount);
        var stepsPerEpoch = Math.Max(1, (int)Math.Ceiling(images / 64d));
        var epochs = Math.Clamp((int)Math.Round((double)TargetOptimizerSteps / stepsPerEpoch / EpochStep) * EpochStep, MinEpochs, MaxEpochs);
        reasons.Add(new YoloAdviceItem(YoloAdviceField.Epochs, "YoloAdviceEpochs", [epochs, images, stepsPerEpoch * epochs]));
        return epochs;
    }
}
