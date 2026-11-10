namespace Snet.Yolo.Tasks.Core.Models;

using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

/// <summary>
/// 标注任务：对应 Label Studio 导出的顶层任务 JSON（task_format 文档）。
/// 未知字段经 Extra 保留；空值成员序列化时省略，保证与官方 JSON round-trip 语义一致。
/// </summary>
public sealed class AnnotationTask
{
    /// <summary>任务标识。</summary>
    [JsonPropertyName("id")]
    public long? Id { get; set; }

    /// <summary>任务创建时间（UTC ISO-8601）。</summary>
    [JsonPropertyName("created_at")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public DateTime? CreatedAt { get; set; }

    /// <summary>最近更新时间。</summary>
    [JsonPropertyName("updated_at")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public DateTime? UpdatedAt { get; set; }

    /// <summary>所属工程标识（单机版可空）。</summary>
    [JsonPropertyName("project")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public long? Project { get; set; }

    /// <summary>任务数据（标签配置中以 $field 引用的字段，如 image URL / text / audio 路径）。</summary>
    [JsonPropertyName("data")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public JsonObject? Data { get; set; }

    /// <summary>标注结果列表。</summary>
    [JsonPropertyName("annotations")]
    public List<Annotation> Annotations { get; set; } = new();

    /// <summary>预测列表。</summary>
    [JsonPropertyName("predictions")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<Annotation>? Predictions { get; set; }

    /// <summary>草稿列表（快照导出时出现）。</summary>
    [JsonPropertyName("drafts")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<Annotation>? Drafts { get; set; }

    /// <summary>未建模的额外字段（原样保留）。</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Extra { get; set; }
}
