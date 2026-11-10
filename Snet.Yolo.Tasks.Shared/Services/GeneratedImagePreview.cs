namespace Snet.Yolo.Tasks.Services;

/// <summary>优化结果。</summary>
/// <param name="Data">优化后的字节（JPEG）。</param>
/// <param name="Width">宽。</param>
/// <param name="Height">高。</param>
/// <param name="Quality">最终 JPEG 质量。</param>
/// <param name="OriginalBytes">原始字节数。</param>
/// <param name="Note">面向用户的说明（中文，可空）。</param>
public sealed record GeneratedImagePreview(byte[] Data, int Width, int Height, int Quality, long OriginalBytes, string? Note)
{
    /// <summary>优化后的字节数。</summary>
    public long Bytes => Data.LongLength;
}
