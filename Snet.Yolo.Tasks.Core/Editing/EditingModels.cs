namespace Snet.Yolo.Tasks.Core.Editing;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;

/// <summary>图像像素空间矩形（编辑几何单位，与视口变换同构；序列化转百分数）。</summary>
public readonly record struct PixelRect(double X, double Y, double Width, double Height);

/// <summary>画布推送的区域视图（几何按类型承载；颜色由服务端解析）。</summary>
public sealed class RegionView
{
    /// <summary>区域 id。</summary>
    public string Id { get; set; } = string.Empty;
    /// <summary>region 类型（RegionType 常量：rectanglelabels/polygonlabels/keypointlabels/ellipselabels）。</summary>
    public string Type { get; set; } = string.Empty;
    /// <summary>标签文本（用于回显）。</summary>
    public string LabelText { get; set; } = string.Empty;
    /// <summary>颜色。</summary>
    public string Color { get; set; } = "#40a0ff";
    /// <summary>是否选中。</summary>
    public bool Selected { get; set; }

    // ── rectanglelabels ──
    /// <summary>Rectangle left coordinate in pixels.</summary>
    public double X { get; set; }
    /// <summary>Rectangle top coordinate in pixels.</summary>
    public double Y { get; set; }
    /// <summary>Rectangle width in pixels.</summary>
    public double Width { get; set; }
    /// <summary>Rectangle height in pixels.</summary>
    public double Height { get; set; }
    /// <summary>Clockwise rotation in degrees.</summary>
    public double Rotation { get; set; }

    // ── polygonlabels（像素点序列） ──
    /// <summary>Polygon or brush X coordinates in pixels.</summary>
    public double[] PointsX { get; set; } = Array.Empty<double>();
    /// <summary>Polygon or brush Y coordinates in pixels.</summary>
    public double[] PointsY { get; set; } = Array.Empty<double>();

    // ── keypointlabels ──
    /// <summary>Keypoint X coordinate in pixels.</summary>
    public double Kx { get; set; }
    /// <summary>Keypoint Y coordinate in pixels.</summary>
    public double Ky { get; set; }

    // ── brushlabels（点列 + 笔宽，用于描边渲染） ──
    /// <summary>Brush diameter in pixels.</summary>
    public double BrushSize { get; set; }

    // ── ellipselabels（中心 + 半径） ──
    /// <summary>Ellipse center X coordinate in pixels.</summary>
    public double Ex { get; set; }
    /// <summary>Ellipse center Y coordinate in pixels.</summary>
    public double Ey { get; set; }
    /// <summary>Ellipse horizontal radius in pixels.</summary>
    public double Rx { get; set; }
    /// <summary>Ellipse vertical radius in pixels.</summary>
    public double Ry { get; set; }
}

/// <summary>控制标签标签颜色工具（缺省色板循环）。</summary>
public static class LabelPalette
{
    private static readonly string[] DefaultColors =
    {
        "#e6194b", "#3cb44b", "#ffe119", "#4363d8", "#f58231", "#911eb4",
        "#42d4f4", "#f032e6", "#bfef45", "#fabed4", "#469990", "#dcbeff",
    };

    /// <summary>解析标签背景色；未配置时按序号循环取色板。</summary>
    public static string ResolveColor(IEnumerable<Config.LabelOptionInfo> options, string? labelValue, int index)
    {
        if (!string.IsNullOrEmpty(labelValue))
        {
            var match = options.FirstOrDefault(option => option.Value == labelValue);
            if (match is not null && !string.IsNullOrWhiteSpace(match.Background))
            {
                return match.Background;
            }
        }

        return ColorForIndex(index);
    }

    /// <summary>按稳定序号生成标签颜色；前十二项使用人工色板，之后使用黄金角扩展色板。</summary>
    public static string ColorForIndex(int index)
    {
        var normalizedIndex = index == int.MinValue ? int.MaxValue : Math.Abs(index);
        if (normalizedIndex < DefaultColors.Length) { return DefaultColors[normalizedIndex]; }
        var hue = normalizedIndex * 137.50776405003785 % 360d;
        var saturation = 0.68d + normalizedIndex % 3 * 0.06d;
        var lightness = 0.48d + normalizedIndex % 2 * 0.08d;
        return HslToHex(hue, saturation, lightness);
    }

    /// <summary>从扩展色板中选择一个尚未使用的颜色。</summary>
    public static string NextDistinctColor(IEnumerable<string> usedColors)
    {
        ArgumentNullException.ThrowIfNull(usedColors);
        var used = usedColors.Where(color => !string.IsNullOrWhiteSpace(color)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        for (var index = 0; index < 1_000_000; index++)
        {
            var color = ColorForIndex(index);
            if (!used.Contains(color)) { return color; }
        }
        throw new InvalidOperationException("无法生成新的唯一标签颜色。");
    }

    /// <summary>把 HSL 颜色转换为浏览器颜色输入支持的六位十六进制颜色。</summary>
    private static string HslToHex(double hue, double saturation, double lightness)
    {
        var chroma = (1d - Math.Abs(2d * lightness - 1d)) * saturation;
        var sector = hue / 60d;
        var secondary = chroma * (1d - Math.Abs(sector % 2d - 1d));
        var (red, green, blue) = sector switch
        {
            < 1d => (chroma, secondary, 0d),
            < 2d => (secondary, chroma, 0d),
            < 3d => (0d, chroma, secondary),
            < 4d => (0d, secondary, chroma),
            < 5d => (secondary, 0d, chroma),
            _ => (chroma, 0d, secondary),
        };
        var match = lightness - chroma / 2d;
        return $"#{(int)Math.Round((red + match) * 255d):X2}{(int)Math.Round((green + match) * 255d):X2}{(int)Math.Round((blue + match) * 255d):X2}";
    }
}

/// <summary>行 value 读写辅助（percent JSON）。</summary>
public static class ValueAccess
{
    /// <summary>读字符串数组字段（如 rectanglelabels）。</summary>
    public static List<string> GetStringList(JsonObject value, string fieldName)
        => value[fieldName]?.AsArray().Select(item => item?.GetValue<string>() ?? string.Empty).Where(s => s.Length > 0).ToList() ?? new List<string>();

    /// <summary>写字符串数组字段。</summary>
    public static void SetStringList(JsonObject value, string fieldName, IEnumerable<string> items)
    {
        var array = new JsonArray();
        foreach (var item in items)
        {
            array.Add(item);
        }

        value[fieldName] = array;
    }

    /// <summary>读 double 字段（兼容 int/long/decimal 等数值类型）。</summary>
    public static double GetDouble(JsonObject value, string name)
    {
        if (value[name] is not JsonValue node)
        {
            return 0d;
        }

        if (node.TryGetValue<int>(out var intValue)) { return intValue; }
        if (node.TryGetValue<double>(out var doubleValue)) { return doubleValue; }
        if (node.TryGetValue<long>(out var longValue)) { return longValue; }
        if (node.TryGetValue<decimal>(out var decimalValue)) { return (double)decimalValue; }
        return 0d;
    }

    /// <summary>写 double 字段。</summary>
    public static void SetDouble(JsonObject value, string name, double number) => value[name] = number;
}
