namespace Snet.Yolo.Server.anomalib;

using System.Globalization;
using System.Text.Json;

/// <summary>解析 Python 流水线输出的 <c>VISUALIDENTITY_JSON:</c> 结构化进度行，并更新实时指标。</summary>
public static class AnomalibProgressParser
{
    /// <summary>结构化进度行的固定前缀，必须与 Python 脚本中的 JSON_MARKER 一致。</summary>
    public const string Marker = "VISUALIDENTITY_JSON:";

    /// <summary>判断一行输出是否是结构化进度行（这类行不进入用户可见的日志）。</summary>
    /// <param name="line">子进程输出的一行文本。</param>
    /// <returns>以固定前缀开头时为 true。</returns>
    public static bool IsStructured(string line)
        => line.StartsWith(Marker, StringComparison.Ordinal);

    /// <summary>解析结构化进度行；格式错误或类型未知时忽略，不影响训练。</summary>
    /// <param name="line">子进程输出的一行文本。</param>
    /// <param name="metrics">被更新的实时指标。</param>
    /// <returns>成功识别并应用时为 true。</returns>
    public static bool TryApply(string line, AnomalibLiveMetrics metrics)
    {
        ArgumentNullException.ThrowIfNull(metrics);
        if (!IsStructured(line)) { return false; }
        try
        {
            using var document = JsonDocument.Parse(line[Marker.Length..]);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("type", out var type)) { return false; }
            switch (type.GetString())
            {
                case "progress": ApplyProgress(root, metrics); return true;
                case "plan": ApplyPlan(root, metrics); return true;
                case "epoch": return ApplyEpoch(root, metrics);
                case "calibration": ApplyCalibration(root, metrics); return true;
                default: return false;
            }
        }
        catch (JsonException) { return false; }
    }

    /// <summary>阶段进度：resize、train、extract、coreset。</summary>
    private static void ApplyProgress(JsonElement root, AnomalibLiveMetrics metrics)
    {
        metrics.Stage = ReadString(root, "stage") ?? metrics.Stage;
        metrics.StageCurrent = ReadInt(root, "current") ?? metrics.StageCurrent;
        metrics.StageTotal = ReadInt(root, "total") ?? metrics.StageTotal;
    }

    /// <summary>PatchCore 的显存规划。</summary>
    private static void ApplyPlan(JsonElement root, AnomalibLiveMetrics metrics)
    {
        metrics.PatchCount = ReadLong(root, "patches") ?? metrics.PatchCount;
        metrics.KeepFraction = ReadDouble(root, "keepFraction") ?? metrics.KeepFraction;
        metrics.MemoryBank = ReadInt(root, "memoryBank") ?? metrics.MemoryBank;
        metrics.EmbeddingMiB = ReadLong(root, "embeddingMiB") ?? metrics.EmbeddingMiB;
        metrics.FreeVramMiB = ReadLong(root, "freeVramMiB") ?? metrics.FreeVramMiB;
    }

    /// <summary>EfficientAD 一个轮次结束后的指标；轮次缺失或不是正数时忽略并返回 false。</summary>
    private static bool ApplyEpoch(JsonElement root, AnomalibLiveMetrics metrics)
    {
        var epoch = ReadInt(root, "epoch");
        if (epoch is not > 0) { return false; }
        metrics.Epoch = epoch.Value;
        metrics.TotalEpochs = ReadInt(root, "totalEpochs") ?? metrics.TotalEpochs;
        metrics.TrainLoss = ReadDouble(root, "trainLoss");
        metrics.StudentLoss = ReadDouble(root, "studentLoss");
        metrics.AutoencoderLoss = ReadDouble(root, "autoencoderLoss");
        metrics.PixelAuroc = ReadDouble(root, "pixelAuroc");
        metrics.BestPixelAuroc = ReadDouble(root, "bestPixelAuroc") ?? metrics.BestPixelAuroc;
        metrics.BestEpoch = ReadInt(root, "bestEpoch") ?? metrics.BestEpoch;
        metrics.History.Add(new AnomalibEpochPoint { Epoch = epoch.Value, TrainLoss = metrics.TrainLoss, PixelAuroc = metrics.PixelAuroc });
        if (metrics.History.Count > AnomalibLiveMetrics.MaximumHistoryPoints)
        {
            metrics.History.RemoveRange(0, metrics.History.Count - AnomalibLiveMetrics.MaximumHistoryPoints);
        }
        return true;
    }

    /// <summary>正常图阈值标定的结果。</summary>
    private static void ApplyCalibration(JsonElement root, AnomalibLiveMetrics metrics)
    {
        metrics.ImageThreshold = ReadDouble(root, "imageThreshold") ?? metrics.ImageThreshold;
        metrics.NormalScoreMax = ReadDouble(root, "imageScoreMax") ?? metrics.NormalScoreMax;
    }

    /// <summary>读取字符串字段。</summary>
    private static string? ReadString(JsonElement root, string name)
        => root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    /// <summary>读取整数字段；缺失、空值或类型不符时返回空。</summary>
    private static int? ReadInt(JsonElement root, string name)
        => root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var number) ? number : null;

    /// <summary>读取长整数字段。</summary>
    private static long? ReadLong(JsonElement root, string name)
        => root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out var number) ? number : null;

    /// <summary>读取有限浮点字段。</summary>
    private static double? ReadDouble(JsonElement root, string name)
        => root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number
            && value.TryGetDouble(out var number) && double.IsFinite(number) ? number : null;
}
