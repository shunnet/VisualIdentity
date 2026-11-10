namespace Snet.Yolo.Tasks.Core.Models;

using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

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
