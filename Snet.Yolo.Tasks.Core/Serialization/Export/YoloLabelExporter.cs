using Snet.Yolo.Tasks.Core.Config;
using Snet.Yolo.Tasks.Core.Editing;
using Snet.Yolo.Tasks.Core.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json.Nodes;

namespace Snet.Yolo.Tasks.Core.Serialization.Export;

/// <summary>按 YOLO 任务类型生成标签行（detect=bbox、obb=bbox+angle、segment=多边形点列、pose=关键点）。坐标统一 0-1 归一化。</summary>
public static class YoloLabelExporter
{
    public static string BuildModelName(YoloTaskType task, string baseModel) => baseModel;

    /// <summary>为单个任务生成多行标签文本（\n 结尾）。</summary>
    public static string Build(AnnotationTask task, YoloTaskType taskType, IReadOnlyList<string> classes)
    {
        var ann = task.Annotations.FirstOrDefault(a => a.WasCancelled != true);
        if (ann is null) { return string.Empty; }
        var labelField = LabelField(taskType);
        if (taskType == YoloTaskType.Pose) { return BuildPose(ann, labelField, classes); }
        var sb = new StringBuilder();
        foreach (var row in ann.Result)
        {
            if (row.Type is RegionType.Labels or RegionType.Choices or RegionType.TextArea) { continue; }
            var label = row.Value is null ? null : ValueAccess.GetStringList(row.Value, labelField).FirstOrDefault();
            if (label is null) { label = row.Value is null ? null : ValueAccess.GetStringList(row.Value, "labels").FirstOrDefault(); }
            if (label is null) { continue; }
            var idx = IndexOf(classes, label);
            if (idx < 0) { continue; }
            var line = BuildLine(row, idx, taskType);
            if (line is not null) { sb.AppendLine(line); }
        }
        return sb.ToString();
    }

    private static string? BuildLine(ResultRow row, int idx, YoloTaskType taskType)
    {
        if (row.Value is null) { return null; }
        // bbox 系（detect / obb）
        if (taskType is YoloTaskType.Detect or YoloTaskType.Obb)
        {
            if (row.Type != RegionType.RectangleLabels) { return null; }
            var x = ValueAccess.GetDouble(row.Value, "x");
            var y = ValueAccess.GetDouble(row.Value, "y");
            var w = ValueAccess.GetDouble(row.Value, "width");
            var h = ValueAccess.GetDouble(row.Value, "height");
            var cx = (x + w / 2d) / 100d;
            var cy = (y + h / 2d) / 100d;
            var ww = w / 100d; var hh = h / 100d;
            if (taskType == YoloTaskType.Obb)
            {
                var rotationDeg = ValueAccess.GetDouble(row.Value, "rotation");
                var angleRad = rotationDeg * Math.PI / 180d;
                return $"{idx} {F(cx)} {F(cy)} {F(ww)} {F(hh)} {F(angleRad)}";
            }
            return $"{idx} {F(cx)} {F(cy)} {F(ww)} {F(hh)}";
        }
        // segment
        if (taskType == YoloTaskType.Segment)
        {
            if (row.Type is not RegionType.PolygonLabels and not RegionType.BrushLabels) { return null; }
            var xs = ReadPointArray(row.Value, "pointxs");
            var ys = ReadPointArray(row.Value, "pointys");
            if (xs.Length == 0 || xs.Length != ys.Length) { return null; }
            var sb = new StringBuilder();
            sb.Append(idx).Append(' ');
            for (var i = 0; i < xs.Length; i++) { sb.Append(F(xs[i] / 100d)).Append(' ').Append(F(ys[i] / 100d)).Append(' '); }
            return sb.ToString().TrimEnd();
        }
        return null;
    }

    /// <summary>姿态导出（Ultralytics pose 每行 = 一个对象 + 全部关键点）：同一类别的关键点按出现顺序合并为一行 cls x1 y1 v x2 y2 v ...。</summary>
    private static string BuildPose(Models.Annotation ann, string labelField, IReadOnlyList<string> classes)
    {
        var groups = new SortedDictionary<int, List<(double X, double Y)>>();
        foreach (var row in ann.Result)
        {
            if (row.Type != RegionType.KeyPointLabels || row.Value is null) { continue; }
            var label = ValueAccess.GetStringList(row.Value, labelField).FirstOrDefault();
            if (string.IsNullOrEmpty(label)) { label = ValueAccess.GetStringList(row.Value, "labels").FirstOrDefault(); }
            if (string.IsNullOrEmpty(label)) { continue; }
            var idx = IndexOf(classes, label);
            if (idx < 0) { continue; }
            var x = ValueAccess.GetDouble(row.Value, "x");
            var y = ValueAccess.GetDouble(row.Value, "y");
            if (!groups.TryGetValue(idx, out var list)) { list = new List<(double, double)>(); groups[idx] = list; }
            list.Add((x / 100d, y / 100d));
        }
        var sb = new StringBuilder();
        foreach (var kv in groups)
        {
            var pts = kv.Value;
            var minX = pts.Min(p => p.X); var maxX = pts.Max(p => p.X);
            var minY = pts.Min(p => p.Y); var maxY = pts.Max(p => p.Y);
            // Ultralytics pose 行 = cls cx cy w h + n×(x y v)；bbox 取关键点外接框（最小 1.5% 防退化为点）
            var w = Math.Max(maxX - minX, 0.015d);
            var h = Math.Max(maxY - minY, 0.015d);
            var cx = (minX + maxX) / 2d;
            var cy = (minY + maxY) / 2d;
            sb.Append(kv.Key).Append(' ')
              .Append(F(cx)).Append(' ').Append(F(cy)).Append(' ').Append(F(w)).Append(' ').Append(F(h));
            foreach (var (x, y) in pts) { sb.Append(' ').Append(F(x)).Append(' ').Append(F(y)).Append(" 2"); }
            sb.AppendLine();
        }
        return sb.ToString();
    }

    private static string LabelField(YoloTaskType taskType) => taskType switch
    {
        YoloTaskType.Segment => "polygonlabels",
        YoloTaskType.Pose => "keypointlabels",
        _ => "rectanglelabels",
    };

    private static double[] ReadPointArray(JsonObject value, string name)
    {
        if (value[name]?.AsArray() is not { } arr) { return Array.Empty<double>(); }
        var list = new List<double>();
        foreach (var n in arr)
        {
            if (n is null) { list.Add(0); continue; }
            try { list.Add(Convert.ToDouble(n.GetValue<object>(), System.Globalization.CultureInfo.InvariantCulture)); }
            catch { list.Add(0); }
        }
        return list.ToArray();
    }

    private static int IndexOf(IReadOnlyList<string> classes, string label)
    {
        for (var i = 0; i < classes.Count; i++) { if (string.Equals(classes[i], label, StringComparison.Ordinal)) { return i; } }
        return -1;
    }

    private static string F(double v) => v.ToString("0.#####", System.Globalization.CultureInfo.InvariantCulture);
}
