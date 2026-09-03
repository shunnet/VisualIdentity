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

/// <summary>一条标注结果（region）：对应 LS result[] 元素。</summary>
public sealed class ResultRow
{
    /// <summary>region 标识。</summary>
    [JsonPropertyName("id")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Id { get; set; }

    /// <summary>region 类型（见 RegionType）。</summary>
    [JsonPropertyName("type")]
    public string Type { get; set; } = string.Empty;

    /// <summary>来源 control tag 名。</summary>
    [JsonPropertyName("from_name")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? FromName { get; set; }

    /// <summary>目标 object tag 名。</summary>
    [JsonPropertyName("to_name")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ToName { get; set; }

    /// <summary>区域数值（结构随 type 变化，见 §5.2）。</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("value")]
    public JsonObject? Value { get; set; }

    /// <summary>父 region id（层级：如关键点挂到矩形框）。</summary>
    [JsonPropertyName("parentID")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ParentId { get; set; }

    /// <summary>来源：manual / prediction / auto。</summary>
    [JsonPropertyName("origin")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Origin { get; set; }

    /// <summary>对象引用（如 $image）。</summary>
    [JsonPropertyName("source")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Source { get; set; }

    /// <summary>图像旋转角（度）。</summary>
    [JsonPropertyName("image_rotation")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public double? ImageRotation { get; set; }

    /// <summary>图像原始宽（像素）。</summary>
    [JsonPropertyName("original_width")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public double? OriginalWidth { get; set; }

    /// <summary>图像原始高（像素）。</summary>
    [JsonPropertyName("original_height")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public double? OriginalHeight { get; set; }

    /// <summary>音频原始时长（秒）。</summary>
    [JsonPropertyName("original_length")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public double? OriginalLength { get; set; }

    /// <summary>附加元数据。</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("meta")]
    public JsonObject? Meta { get; set; }

    /// <summary>未建模字段（prediction 的 score 等）。</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Extra { get; set; }
}
