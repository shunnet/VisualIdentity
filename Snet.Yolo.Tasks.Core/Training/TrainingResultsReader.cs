using System.Globalization;

namespace Snet.Yolo.Tasks.Core.Training;

/// <summary>一次训练的结果摘要（来自 Ultralytics 输出的 results.csv）。</summary>
/// <param name="Epochs">已完成的轮数。</param>
/// <param name="Map50">各轮中最好的 mAP50。</param>
/// <param name="Map5095">各轮中最好的 mAP50-95。</param>
/// <param name="Precision">最好 mAP50 那一轮的精确率。</param>
/// <param name="Recall">最好 mAP50 那一轮的召回率。</param>
public sealed record TrainingResultSummary(int Epochs, double Map50, double Map5095, double Precision, double Recall);

/// <summary>
/// 读取 Ultralytics 训练输出目录里的 results.csv，判断这次训练到底有没有学到东西。
/// 训练日志里的 mAP 行滚动很快、容易看漏；results.csv 是留档的权威指标。
/// </summary>
public static class TrainingResultsReader
{
    /// <summary>mAP50 低于此值即视为"什么都没学到"。</summary>
    public const double LearnedThreshold = 0.0005d;

    /// <summary>mAP50 低于此值即视为"只能勉强识别、还不能用"。</summary>
    public const double WeakMap50Threshold = 0.5d;

    /// <summary>
    /// 是否需要再做一次"训练集自检"（在训练图片上按界面默认置信度复验）。
    /// 验证集 mAP 可信时不必多跑一遍（大工程上这次复验很贵）；
    /// 只有验证集太小、指标缺失或指标明显偏低时，才需要这条确定性的结论。
    /// </summary>
    public static bool NeedsTrainSetSelfCheck(TrainingResultSummary? summary, int valImageCount)
        => summary is null
        || summary.Map50 < WeakMap50Threshold
        || valImageCount < DatasetHealthCheck.MinValImages;

    /// <summary>读取 results.csv；文件缺失或格式不符时返回 null。</summary>
    public static TrainingResultSummary? Read(string resultsCsvPath)
    {
        if (string.IsNullOrWhiteSpace(resultsCsvPath) || !File.Exists(resultsCsvPath)) { return null; }
        string[] lines;
        try { lines = File.ReadAllLines(resultsCsvPath); }
        catch (IOException) { return null; }
        if (lines.Length < 2) { return null; }
        var header = Split(lines[0]);
        var map50Index = IndexOf(header, "metrics/mAP50(B)");
        if (map50Index < 0) { return null; }
        var map5095Index = IndexOf(header, "metrics/mAP50-95(B)");
        var precisionIndex = IndexOf(header, "metrics/precision(B)");
        var recallIndex = IndexOf(header, "metrics/recall(B)");
        double? best = null, best5095 = null, bestPrecision = null, bestRecall = null;
        var epochs = 0;
        foreach (var line in lines.Skip(1))
        {
            if (string.IsNullOrWhiteSpace(line)) { continue; }
            var fields = Split(line);
            if (fields.Length <= map50Index) { continue; }
            if (!TryDouble(fields[map50Index], out var map50)) { continue; }
            epochs++;
            if (best is not null && map50 <= best.Value) { continue; }
            best = map50;
            best5095 = map5095Index >= 0 && map5095Index < fields.Length && TryDouble(fields[map5095Index], out var v95) ? v95 : null;
            bestPrecision = precisionIndex >= 0 && precisionIndex < fields.Length && TryDouble(fields[precisionIndex], out var p) ? p : null;
            bestRecall = recallIndex >= 0 && recallIndex < fields.Length && TryDouble(fields[recallIndex], out var r) ? r : null;
        }
        return best is null ? null : new TrainingResultSummary(epochs, best.Value, best5095 ?? 0d, bestPrecision ?? 0d, bestRecall ?? 0d);
    }

    /// <summary>本次训练是否等于"没学到东西"（mAP50 ≈ 0）。</summary>
    public static bool LearnedNothing(TrainingResultSummary? summary)
        => summary is not null && summary.Map50 <= LearnedThreshold;

    /// <summary>训练结果一句话摘要。</summary>
    public static string Describe(TrainingResultSummary summary)
        => $"训练结果：{summary.Epochs} 轮，最佳 mAP50 = {summary.Map50.ToString("0.####", CultureInfo.InvariantCulture)}，"
         + $"mAP50-95 = {summary.Map5095.ToString("0.####", CultureInfo.InvariantCulture)}，"
         + $"精确率 = {summary.Precision.ToString("0.###", CultureInfo.InvariantCulture)}，"
         + $"召回率 = {summary.Recall.ToString("0.###", CultureInfo.InvariantCulture)}。";

    /// <summary>mAP50 为 0 时的原因提示（已按可能性排序）。</summary>
    public static string ExplainLearnedNothing(DatasetStats? stats, int epochs)
    {
        var reasons = new List<string>();
        if (stats is not null && stats.SparseClasses + stats.EmptyClasses > 0)
        {
            reasons.Add($"标注太少的类别有 {stats.SparseClasses + stats.EmptyClasses} 个（每类建议 ≥ {DatasetHealthCheck.MinInstancesPerClass} 个实例）");
        }
        if (stats is not null && stats.ImageCount < DatasetHealthCheck.MinImages)
        {
            reasons.Add($"图片只有 {stats.ImageCount} 张（建议 ≥ {DatasetHealthCheck.MinImages} 张）");
        }
        if (stats is not null && stats.HasBoxes && stats.BoxSizesPixels.Count > 0 && stats.MinBoxPixels < DatasetHealthCheck.MinBoxPixels)
        {
            reasons.Add("目标相对整图太小，需要调大图像尺寸（imgsz）或先把大图切小再标注");
        }
        if (stats is not null && stats.BoxCount > 0 && stats.BoxCount < DatasetHealthCheck.MinInstancesPerClass * 2)
        {
            reasons.Add($"全部标注框一共只有 {stats.BoxCount} 个");
        }
        reasons.Add($"训练轮数可能不够（当前 {epochs} 轮，图片少时建议 300 轮以上）");
        reasons.Add("标注本身有误或不同类别外观高度相似（同一张图里相似的目标被标成了不同类别）");
        var hint = stats is not null && stats.ValImageCount < DatasetHealthCheck.MinValImages
            ? $"另外验证集只有 {stats.ValImageCount} 张，mAP 指标本身也不可靠，请以实际识别效果为准。"
            : "";
        return "本次训练的最佳 mAP50 为 0，说明模型没有学到有效特征：这样的模型部署后会几乎识别不到任何目标。可能原因（按可能性排序）："
            + string.Join("；", reasons.Select((r, i) => $"{i + 1}) {r}")) + "。" + hint;
    }

    private static string[] Split(string line) => line.Split(',').Select(f => f.Trim()).ToArray();

    private static int IndexOf(string[] header, string name)
    {
        for (var i = 0; i < header.Length; i++) { if (string.Equals(header[i], name, StringComparison.Ordinal)) { return i; } }
        return -1;
    }

    private static bool TryDouble(string text, out double value)
        => double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value);
}
