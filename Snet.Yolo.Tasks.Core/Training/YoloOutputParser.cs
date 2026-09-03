using System;

namespace Snet.Yolo.Tasks.Core.Training;

/// <summary>解析 Ultralytics 训练日志：epoch 进度与 mAP/loss 指标。</summary>
public static class YoloOutputParser
{
    /// <summary>尝试从一行日志提取进度信息（epoch/total 为核心，尽力抓取 loss）；失败返回 null。</summary>
    public static TrainingProgressUpdate? Parse(string line)
    {
        var m = System.Text.RegularExpressions.Regex.Match(line, @"^\s*(?<e>\d+)\s*/\s*(?<t>\d+)\s+");
        if (!m.Success) { return null; }
        if (!int.TryParse(m.Groups["e"].Value, out var epoch) || !int.TryParse(m.Groups["t"].Value, out var total)) { return null; }
        double? box = null, cls = null, dfl = null;
        var nums = System.Text.RegularExpressions.Regex.Matches(line, @"(?<![0-9.\-])-?\d+\.\d+");
        if (nums.Count >= 3)
        {
            box = double.TryParse(nums[nums.Count - 3].Value, out var b) ? b : null;
            cls = double.TryParse(nums[nums.Count - 2].Value, out var cl) ? cl : null;
            dfl = double.TryParse(nums[nums.Count - 1].Value, out var d) ? d : null;
        }
        return new TrainingProgressUpdate(epoch, total, box, cls, dfl, null, null);
    }

    /// <summary>解析验证/训练末段的 mAP 行。</summary>
    public static TrainingProgressUpdate? ParseMetrics(string line)
    {
        var m = System.Text.RegularExpressions.Regex.Match(line, @"all\s+([\d.]+)\s+([\d.]+)\s+([\d.]+)\s+(?:[\d.]+\s+)?([\d.]+)");
        if (!m.Success) { return null; }
        return new TrainingProgressUpdate(null, null, null, null, null,
            double.TryParse(m.Groups[1].Value, out var p) ? p : null,
            double.TryParse(m.Groups[2].Value, out var r) ? r : null);
    }
}

/// <summary>一次进度更新。</summary>
public sealed record TrainingProgressUpdate(int? Epoch, int? TotalEpochs, double? BoxLoss, double? ClsLoss, double? DflLoss, double? Precision, double? Recall)
{
    public int Percent => TotalEpochs is > 0 && Epoch is not null ? (int)Math.Round(Epoch.Value * 100.0 / TotalEpochs.Value) : 0;
}
