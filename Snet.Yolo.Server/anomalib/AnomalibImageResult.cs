namespace Snet.Yolo.Server.anomalib;

/// <summary>
/// Anomalib 单张图片推理结果。
/// </summary>
public sealed class AnomalibImageResult
{
    /// <summary>获取或初始化整图异常分数。</summary>
    public required float ImageScore { get; init; }

    /// <summary>获取或初始化整图是否被判定为异常。</summary>
    public required bool IsAnomalous { get; init; }

    /// <summary>获取或初始化原图宽度。</summary>
    public required int OriginalWidth { get; init; }

    /// <summary>获取或初始化原图高度。</summary>
    public required int OriginalHeight { get; init; }

    /// <summary>获取或初始化异常图宽度。</summary>
    public required int MapWidth { get; init; }

    /// <summary>获取或初始化异常图高度。</summary>
    public required int MapHeight { get; init; }

    /// <summary>获取或初始化纯模型推理耗时，单位为毫秒。</summary>
    public required long InferenceMilliseconds { get; init; }

    /// <summary>获取或初始化映射到原图坐标的异常区域。</summary>
    public required IReadOnlyList<AnomalibRegionResult> Regions { get; init; }
}
