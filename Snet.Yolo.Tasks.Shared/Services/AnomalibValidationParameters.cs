namespace Snet.Yolo.Tasks.Services;

/// <summary>Anomalib 验证时使用的判定阈值和最小异常区域。</summary>
public sealed record AnomalibValidationParameters(float Threshold, int MinimumArea)
{
    /// <summary>清理磁盘读回的参数，并用训练阈值替代非有限值。</summary>
    public AnomalibValidationParameters Normalize()
        => new(float.IsFinite(Threshold) ? Math.Clamp(Threshold, 0, 1) : AnomalibValidationState.DefaultThreshold,
            Math.Clamp(MinimumArea, 1, 16_777_216));
}
