namespace Snet.Yolo.Server.anomalib;

using System.Text.Json;
using System.Text.Json.Serialization;

/// <summary>
/// Python 流水线在产物目录写出的 <c>metrics.json</c>：设备、版本、显存峰值、阶段耗时、轮次曲线与 PatchCore 显存规划。
/// 训练成功或失败都会写出；字段缺失时按空值处理，解析失败不影响训练结果。
/// </summary>
public sealed class AnomalibRunMetricsFile
{
    /// <summary>宽松的读取选项：忽略未知字段，字段名大小写不敏感。</summary>
    private static readonly JsonSerializerOptions ReadOptions = new() { PropertyNameCaseInsensitive = true };

    /// <summary>训练所用显卡名称；CPU 训练为空。</summary>
    [JsonPropertyName("gpuName")] public string? GpuName { get; init; }

    /// <summary>训练期间 GPU 显存分配峰值（MiB）。</summary>
    [JsonPropertyName("peakVramMiB")] public long? PeakVramMiB { get; init; }

    /// <summary>GPU 总显存（MiB）。</summary>
    [JsonPropertyName("totalVramMiB")] public long? TotalVramMiB { get; init; }

    /// <summary>训练进程的 PyTorch 版本。</summary>
    [JsonPropertyName("torchVersion")] public string? TorchVersion { get; init; }

    /// <summary>EfficientAD 实际完成的轮数。</summary>
    [JsonPropertyName("completedEpochs")] public int? CompletedEpochs { get; init; }

    /// <summary>EfficientAD 最佳模型所在轮次。</summary>
    [JsonPropertyName("bestEpoch")] public int? BestEpoch { get; init; }

    /// <summary>EfficientAD 最佳验证像素 AUROC。</summary>
    [JsonPropertyName("bestPixelAuroc")] public double? BestPixelAuroc { get; init; }

    /// <summary>EfficientAD 的停止原因说明。</summary>
    [JsonPropertyName("stopReason")] public string? StopReason { get; init; }

    /// <summary>各阶段耗时（秒）。</summary>
    [JsonPropertyName("timingsSeconds")] public Dictionary<string, double> TimingsSeconds { get; init; } = [];

    /// <summary>EfficientAD 的轮次曲线。</summary>
    [JsonPropertyName("epochs")] public List<AnomalibRunMetricsEpoch> Epochs { get; init; } = [];

    /// <summary>PatchCore 的显存规划；其他算法为空。</summary>
    [JsonPropertyName("patchcorePlan")] public AnomalibRunMetricsPlan? PatchcorePlan { get; init; }

    /// <summary>流水线失败时的错误说明；成功时为空。</summary>
    [JsonPropertyName("error")] public string? Error { get; init; }

    /// <summary>读取 <c>metrics.json</c>；文件缺失或内容损坏时返回空。</summary>
    /// <param name="path">metrics.json 的绝对路径。</param>
    /// <returns>解析结果；失败时为空。</returns>
    public static AnomalibRunMetricsFile? TryRead(string path)
    {
        try
        {
            return File.Exists(path) ? JsonSerializer.Deserialize<AnomalibRunMetricsFile>(File.ReadAllText(path), ReadOptions) : null;
        }
        catch (Exception error) when (error is JsonException or IOException or UnauthorizedAccessException) { return null; }
    }
}
