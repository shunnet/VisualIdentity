namespace Snet.Yolo.Server.anomalib;

/// <summary>
/// 整数像素矩形，采用左上角坐标和宽高表示。
/// </summary>
public readonly record struct PixelRectangle
{
    /// <summary>
    /// 创建像素矩形。
    /// </summary>
    /// <param name="x">左上角横坐标。</param>
    /// <param name="y">左上角纵坐标。</param>
    /// <param name="width">矩形宽度。</param>
    /// <param name="height">矩形高度。</param>
    public PixelRectangle(int x, int y, int width, int height)
    {
        if (x < 0 || y < 0 || width <= 0 || height <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(width), "像素矩形坐标不能为负数，宽高必须大于零。");
        }
        X = x;
        Y = y;
        Width = width;
        Height = height;
    }

    /// <summary>获取左上角横坐标。</summary>
    public int X { get; }

    /// <summary>获取左上角纵坐标。</summary>
    public int Y { get; }

    /// <summary>获取矩形宽度。</summary>
    public int Width { get; }

    /// <summary>获取矩形高度。</summary>
    public int Height { get; }

    /// <summary>获取不包含在矩形内的右边界坐标。</summary>
    public int Right => checked(X + Width);

    /// <summary>获取不包含在矩形内的下边界坐标。</summary>
    public int Bottom => checked(Y + Height);
}
