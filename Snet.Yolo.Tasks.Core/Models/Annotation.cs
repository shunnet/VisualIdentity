namespace Snet.Yolo.Tasks.Core.Models;

using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

/// <summary>标注/预测/草稿共用结构。</summary>
public sealed class Annotation
{
    /// <summary>标注标识（LS 允许字符串或数字，用 JsonNode 原样承载）。</summary>
    [JsonPropertyName("id")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public JsonNode? Id { get; set; }

    /// <summary>region 结果行。</summary>
    [JsonPropertyName("result")]
    public List<ResultRow> Result { get; set; } = new();

    /// <summary>是否被跳过/取消。</summary>
    [JsonPropertyName("was_cancelled")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? WasCancelled { get; set; }

    /// <summary>是否 ground truth。</summary>
    [JsonPropertyName("ground_truth")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? GroundTruth { get; set; }

    /// <summary>标注耗时（秒）。</summary>
    [JsonPropertyName("lead_time")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public double? LeadTime { get; set; }

    /// <summary>创建时间。</summary>
    [JsonPropertyName("created_at")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public DateTime? CreatedAt { get; set; }

    /// <summary>更新时间。</summary>
    [JsonPropertyName("updated_at")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public DateTime? UpdatedAt { get; set; }

    /// <summary>完成时间。</summary>
    [JsonPropertyName("completed_at")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public DateTime? CompletedAt { get; set; }

    /// <summary>标注员（数字 id 或对象，原样承载）。</summary>
    [JsonPropertyName("completed_by")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public JsonNode? CompletedBy { get; set; }

    /// <summary>结果条数冗余字段。</summary>
    [JsonPropertyName("result_count")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public long? ResultCount { get; set; }

    /// <summary>所属任务 id。</summary>
    [JsonPropertyName("task")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public long? TaskId { get; set; }

    /// <summary>预测/审核等未建模字段。</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Extra { get; set; }
}
