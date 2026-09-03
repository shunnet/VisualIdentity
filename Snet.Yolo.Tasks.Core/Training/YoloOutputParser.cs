using System;

namespace Snet.Yolo.Tasks.Core.Training;

/// <summary>解析 Ultralytics 训练日志：epoch 进度与 mAP/loss 指标。</summary>
public static class YoloOutputParser
{
    private static readonly System.Text.RegularExpressions.Regex AnsiRe = new(@"\x1B\[[0-9;?]*[A-Za-z]");

    /// <summary>剥离 ANSI 转义序列（tqdm 进度行常以 ESC[K 等前缀刷新，会破坏正则匹配）。</summary>
    private static string StripAnsi(string line)
    {
        if (line.IndexOf('\x1B') < 0) { return line; }
        return AnsiRe.Replace(line, "");
    }

    /// <summary>尝试从一行日志提取进度信息（epoch/total 为核心，尽力抓取 loss）；失败返回 null。</summary>
    public static TrainingProgressUpdate? Parse(string line)
    {
        line = StripAnsi(line);
        var m = System.Text.RegularExpressions.Regex.Match(line, @"^\s*(?<e>\d+)\s*/\s*(?<t>\d+)\s+");
        if (!m.Success) { return null; }
        if (!int.TryParse(m.Groups["e"].Value, out var epoch) || !int.TryParse(m.Groups["t"].Value, out var total)) { return null; }
        double? box = null, cls = null, dfl = null;
        // 行格式: 1/50  0.5G  1.227  11.73  0.00862  2  640: 100% ... 1.3it/s 0.8s
        // GPU_mem 后紧跟 box/cls/dfl 三个值；行尾的 it/s、时长不能混入。
        var lm = System.Text.RegularExpressions.Regex.Match(line, @"/\s*\d+\s+[\d.]+\s*G\s+([\d.]+)\s+([\d.]+)\s+([\d.]+)");
        if (lm.Success)
        {
            box = double.TryParse(lm.Groups[1].Value, out var b) ? b : null;
            cls = double.TryParse(lm.Groups[2].Value, out var cl) ? cl : null;
            dfl = double.TryParse(lm.Groups[3].Value, out var d) ? d : null;
        }
        return new TrainingProgressUpdate(epoch, total, box, cls, dfl, null, null);
    }

    /// <summary>解析验证/训练末段的 mAP 行。</summary>
    public static TrainingProgressUpdate? ParseMetrics(string line)
    {
        line = StripAnsi(line);
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
