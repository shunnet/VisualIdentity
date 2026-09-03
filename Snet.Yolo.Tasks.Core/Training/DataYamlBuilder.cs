namespace Snet.Yolo.Tasks.Core.Training;

using System.Collections.Generic;
using System.Linq;

/// <summary>生成 YOLO 数据集的 data.yaml。</summary>
public static class DataYamlBuilder
{
    /// <summary>
    /// 生成 data.yaml。train 始终指向 "images"；val 在 UseVal 且存在 val 目录时指向 "val/images"。
    /// 输出由调用方解析版本名（names 顺序即类别索引），nc = 类别数。
    /// </summary>
    public static string Build(IReadOnlyList<string> classNames, string rootPath, bool useVal, bool hasValDir, int keypointCount = 0)
    {
        var names = string.Join(", ", classNames.Select(n => "\"" + Escape(n) + "\""));
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("path: " + rootPath.Replace('\\', '/').Replace("\\", "\\"));
        sb.AppendLine("train: images");
        sb.AppendLine("val: " + (useVal && hasValDir ? "val/images" : "images"));
        sb.AppendLine("nc: " + classNames.Count);
        sb.AppendLine("names: [" + names + "]");
        if (keypointCount > 0) { sb.AppendLine("kpt_shape: [" + keypointCount + ", 3]"); }
        return sb.ToString();
    }

    private static string Escape(string name) => name.Replace("\\", "\\\\").Replace("\"", "\\\"");
}
