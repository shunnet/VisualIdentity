namespace Snet.Yolo.Server.anomalib;

/// <summary>推理时的区域过滤参数，不修改模型的整图判定或注册门禁。</summary>
public sealed class AnomalibInferenceOptions
{
    /// <summary>区域像素异常分数阈值；为空时使用模型导出的二值掩码。</summary>
    public float? PixelThreshold { get; init; }

    /// <summary>
    /// 整图判定阈值（归一化分数，模型自带的判定点约为 0.5）：整图分数大于等于它判为异常；为空时使用模型自带的判定。
    /// 调低可提高检出率（也会增加误报），调高反之，无需重新训练即可按现场需要调节灵敏度。
    /// </summary>
    public float? ImageThreshold { get; init; }

    /// <summary>保留区域所需的最少异常图像素数，不是原图外接框面积。</summary>
    public int MinimumArea { get; init; } = 4;

    /// <summary>校验阈值和最小区域面积。</summary>
    public void Validate()
    {
        if (PixelThreshold is { } threshold && (!float.IsFinite(threshold) || threshold is < 0 or > 1))
        { throw new ArgumentOutOfRangeException(nameof(PixelThreshold), "区域异常阈值必须在 0 到 1 之间。"); }
        if (ImageThreshold is { } imageThreshold && (!float.IsFinite(imageThreshold) || imageThreshold is < 0 or > 1))
        { throw new ArgumentOutOfRangeException(nameof(ImageThreshold), "整图判定阈值必须在 0 到 1 之间。"); }
        if (MinimumArea is < 1 or > 16_777_216)
        { throw new ArgumentOutOfRangeException(nameof(MinimumArea), "最小异常面积必须在 1 到 16777216 之间。"); }
    }
}
