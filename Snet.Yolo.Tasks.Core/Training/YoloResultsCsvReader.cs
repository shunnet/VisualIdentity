namespace Snet.Yolo.Tasks.Core.Training;

using Snet.Yolo.Tasks.Core.History;
using System.Globalization;

/// <summary>读取 Ultralytics 输出目录里的 <c>results.csv</c>，提取训练指标页需要的最佳轮次指标与逐轮曲线。</summary>
public static class YoloResultsCsvReader
{
    /// <summary>读取并解析 results.csv。</summary>
    /// <param name="resultsCsvPath">results.csv 的路径。</param>
    /// <returns>解析结果；文件缺失、被占用或没有可识别的指标列时返回空。</returns>
    public static YoloResultsSeries? Read(string resultsCsvPath)
    {
        if (string.IsNullOrWhiteSpace(resultsCsvPath) || !File.Exists(resultsCsvPath)) { return null; }
        string[] lines;
        try { lines = File.ReadAllLines(resultsCsvPath); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException) { return null; }
        if (lines.Length < 2) { return null; }
        var header = Split(lines[0]);
        var columns = new YoloResultsColumns(header);
        if (columns.Map5095 < 0 && columns.Map50 < 0 && columns.Accuracy < 0) { return null; }

        var curve = new List<TrainingCurvePoint>();
        double bestFitness = double.NegativeInfinity;
        string[]? bestRow = null;
        string[]? lastRow = null;
        var bestEpoch = 0;
        foreach (var line in lines.Skip(1))
        {
            if (string.IsNullOrWhiteSpace(line)) { continue; }
            var row = Split(line);
            var epoch = columns.Epoch >= 0 && TryRead(row, columns.Epoch) is { } epochValue ? (int)epochValue : curve.Count + 1;
            var score = TryRead(row, columns.Map5095) ?? TryRead(row, columns.Accuracy);
            var fitness = Fitness(row, columns) ?? double.NegativeInfinity;
            curve.Add(new TrainingCurvePoint { Epoch = epoch, Score = score, Loss = TotalLoss(row, columns) });
            lastRow = row;
            if (fitness >= bestFitness) { bestFitness = fitness; bestRow = row; bestEpoch = epoch; }
        }
        if (bestRow is null || lastRow is null) { return null; }
        return new YoloResultsSeries
        {
            Epochs = curve.Count,
            BestEpoch = bestEpoch,
            Map50 = TryRead(bestRow, columns.Map50),
            Map5095 = TryRead(bestRow, columns.Map5095),
            Precision = TryRead(bestRow, columns.Precision),
            Recall = TryRead(bestRow, columns.Recall),
            Accuracy = TryRead(bestRow, columns.Accuracy),
            BoxLoss = TryRead(lastRow, columns.BoxLoss),
            ClsLoss = TryRead(lastRow, columns.ClsLoss),
            DflLoss = TryRead(lastRow, columns.DflLoss),
            Curve = curve,
        };
    }

    /// <summary>
    /// 与 Ultralytics 选择 best.pt 的 fitness 一致：检测 / 定向框为 0.1·mAP50 + 0.9·mAP50-95；
    /// 实例分割再加上掩码的同一组合，姿态估计再加上关键点的同一组合；分类为 top1 与 top5 的均值（没有 top5 列时取 top1）。
    /// </summary>
    private static double? Fitness(string[] row, YoloResultsColumns columns)
    {
        if (columns.Map5095 >= 0)
        {
            return Group(row, columns.Map50, columns.Map5095) + Group(row, columns.MaskMap50, columns.MaskMap5095) + Group(row, columns.PoseMap50, columns.PoseMap5095);
        }
        var top1 = TryRead(row, columns.Accuracy);
        if (top1 is null) { return null; }
        return TryRead(row, columns.AccuracyTop5) is { } top5 ? (top1.Value + top5) / 2 : top1;
    }

    /// <summary>一组 mAP 对 fitness 的贡献；列不存在时为 0。</summary>
    private static double Group(string[] row, int map50Column, int map5095Column)
        => 0.1 * (TryRead(row, map50Column) ?? 0) + 0.9 * (TryRead(row, map5095Column) ?? 0);

    /// <summary>一行训练损失之和；分类任务使用单一的 <c>train/loss</c>。</summary>
    private static double? TotalLoss(string[] row, YoloResultsColumns columns)
    {
        double? total = null;
        foreach (var index in new[] { columns.BoxLoss, columns.ClsLoss, columns.DflLoss, columns.Loss })
        {
            if (TryRead(row, index) is { } value) { total = (total ?? 0) + value; }
        }
        return total;
    }

    /// <summary>按下标读取一个有限数值；越界、负下标、空白或非数值时返回空。</summary>
    private static double? TryRead(string[] row, int index)
        => index >= 0 && index < row.Length
            && double.TryParse(row[index], NumberStyles.Float, CultureInfo.InvariantCulture, out var value) && double.IsFinite(value)
            ? value : null;

    /// <summary>按逗号拆分并去掉 Ultralytics 为对齐写入的空白。</summary>
    private static string[] Split(string line) => line.Split(',').Select(static field => field.Trim()).ToArray();
}
