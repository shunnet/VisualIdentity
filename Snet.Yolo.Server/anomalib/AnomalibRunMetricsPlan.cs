namespace Snet.Yolo.Server.anomalib;

using System.Text.Json.Serialization;

/// <summary>Python 流水线为 PatchCore 规划的显存预算：patch 数量、抽样比例与记忆库大小。</summary>
public sealed class AnomalibRunMetricsPlan
{
    /// <summary>训练 patch 总数。</summary>
    [JsonPropertyName("patches")] public long Patches { get; init; }

    /// <summary>保留参与 coreset 的 patch 比例。</summary>
    [JsonPropertyName("keepFraction")] public double? KeepFraction { get; init; }

    /// <summary>记忆库大小。</summary>
    [JsonPropertyName("memoryBank")] public int MemoryBank { get; init; }

    /// <summary>全部 patch 嵌入的预估显存（MiB）。</summary>
    [JsonPropertyName("embeddingMiB")] public long EmbeddingMiB { get; init; }

    /// <summary>训练开始时 GPU 的可用显存（MiB）。</summary>
    [JsonPropertyName("freeVramMiB")] public long? FreeVramMiB { get; init; }
}
