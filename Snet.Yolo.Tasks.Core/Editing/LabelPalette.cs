namespace Snet.Yolo.Tasks.Core.Editing;

using System;
using System.Collections.Generic;
using System.Linq;

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
