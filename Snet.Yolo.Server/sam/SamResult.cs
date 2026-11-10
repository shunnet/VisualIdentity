namespace Snet.Yolo.Server.sam;

/// <summary>SAM 原图坐标结果；掩膜为单通道 0/255，轮廓不重复闭合点。</summary>
public sealed class SamResult
{
    /// <summary>原图宽。</summary>
    public required int Width { get; init; }
    /// <summary>原图高。</summary>
    public required int Height { get; init; }
    /// <summary>原图掩膜，按需展开以兼容直接读取的调用方；大图标注请使用工作掩码以避免此分配。</summary>
    public byte[] Mask
    {
        get
        {
            if (_mask is not null) { return _mask; }
            var mask = new byte[checked(Width * Height)];
            var source = WorkingMask.Span;
            for (var y = 0; y < Height; y++)
            {
                var row = (int)((long)y * MaskHeight / Height) * MaskWidth;
                for (var x = 0; x < Width; x++) { mask[y * Width + x] = source[row + (int)((long)x * MaskWidth / Width)]; }
            }
            return _mask = mask;
        }
        init => _mask = value;
    }
    private byte[]? _mask;
    /// <summary>工作尺寸的单通道 0/255 掩码，保留孔洞；无需展开为原图大小。</summary>
    public ReadOnlyMemory<byte> WorkingMask { get; init; }
    /// <summary>工作掩码宽度。</summary>
    public int MaskWidth { get; init; }
    /// <summary>工作掩码高度。</summary>
    public int MaskHeight { get; init; }
    /// <summary>按原图像素坐标查询前景，与原图掩码及 RLE 的最近邻采样保持一致。</summary>
    public bool ContainsPoint(int x, int y)
    {
        if (x < 0 || y < 0 || x >= Width || y >= Height) { return false; }
        if (_mask is not null) { return _mask[y * Width + x] != 0; }
        return WorkingMask.Span[(int)((long)y * MaskHeight / Height) * MaskWidth + (int)((long)x * MaskWidth / Width)] != 0;
    }
    /// <summary>轮廓横坐标。</summary>
    public required double[] PointsX { get; init; }
    /// <summary>轮廓纵坐标。</summary>
    public required double[] PointsY { get; init; }
    /// <summary>外接矩形左边。</summary>
    public required double X { get; init; }
    /// <summary>外接矩形上边。</summary>
    public required double Y { get; init; }
    /// <summary>外接矩形宽。</summary>
    public required double BoxWidth { get; init; }
    /// <summary>外接矩形高。</summary>
    public required double BoxHeight { get; init; }
    /// <summary>解码器质量评分，不是类别置信度。</summary>
    public required float Score { get; init; }
    /// <summary>透明预览图，缩小后的掩膜仅用于显示。</summary>
    public required string PreviewDataUrl { get; init; }
}
