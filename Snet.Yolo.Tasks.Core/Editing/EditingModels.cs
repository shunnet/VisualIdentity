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
    public double X { get; set; }
    public double Y { get; set; }
    public double Width { get; set; }
    public double Height { get; set; }
    public double Rotation { get; set; }

    // ── polygonlabels（像素点序列） ──
    public double[] PointsX { get; set; } = Array.Empty<double>();
    public double[] PointsY { get; set; } = Array.Empty<double>();

    // ── keypointlabels ──
    public double Kx { get; set; }
    public double Ky { get; set; }

    // ── brushlabels（点列 + 笔宽，用于描边渲染） ──
    public double BrushSize { get; set; }

    // ── ellipselabels（中心 + 半径） ──
    public double Ex { get; set; }
    public double Ey { get; set; }
    public double Rx { get; set; }
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

        return DefaultColors[Math.Abs(index) % DefaultColors.Length];
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
