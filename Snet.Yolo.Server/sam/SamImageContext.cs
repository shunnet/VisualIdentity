namespace Snet.Yolo.Server.sam;

/// <summary>单张图片的编码结果，仅由当前标注页面持有，不进入跨用户缓存。</summary>
public sealed class SamImageContext
{
    /// <summary>编码时锁定的文件版本；更新后仍使用原解码器，避免特征与权重混用。</summary>
    internal SamInstalledModel? Installed { get; init; }
    /// <summary>编码所使用的模型，切换模型时不得复用另一模型的编码。</summary>
    public SamModelKind ModelKind { get; internal init; }
    /// <summary>编码使用的 GPU 编号；null 表示 CPU，解码沿用同一设备。</summary>
    public int? GpuId { get; internal init; }
    internal float[] Embedding { get; init; } = [];
    internal float[] HighRes0 { get; init; } = [];
    internal float[] HighRes1 { get; init; } = [];
    internal int WorkWidth { get; init; }
    internal int WorkHeight { get; init; }
    /// <summary>原图宽。</summary>
    public int Width { get; internal init; }
    /// <summary>原图高。</summary>
    public int Height { get; internal init; }
}
