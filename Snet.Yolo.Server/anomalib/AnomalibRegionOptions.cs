using System.Buffers;

namespace Snet.Yolo.Server.anomalib;

/// <summary>
/// 异常掩码区域提取选项。
/// </summary>
public sealed class AnomalibRegionOptions
{
    /// <summary>获取或设置保留区域所需的最小异常像素数。</summary>
    public int MinimumArea { get; set; } = 1;

    /// <summary>获取或设置可合并区域之间允许的最大横纵像素间距；零表示不执行区域合并。</summary>
    public int MergeDistance { get; set; }

    /// <summary>获取或设置外接框在每个方向按自身宽高扩展的比例。</summary>
    public double PaddingRatio { get; set; }
}
