namespace Snet.Yolo.Tasks.Core.Serialization;

using Snet.Yolo.Tasks.Core.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;

/// <summary>
/// Label Studio 任务 JSON 的（反）序列化服务，字段名与官方 JSON 完全一致，
/// 未知字段经 JsonExtensionData 保留以实现无损 round-trip。
/// </summary>
public static class TaskJson
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = false,
        PropertyNameCaseInsensitive = false,
    };

    /// <summary>反序列化单个任务 JSON 对象。</summary>
    public static AnnotationTask? DeserializeTask(string json) => JsonSerializer.Deserialize<AnnotationTask>(json, Options);

    /// <summary>序列化单个任务。</summary>
    public static string SerializeTask(AnnotationTask task) => JsonSerializer.Serialize(task, Options);

    /// <summary>反序列化任务数组（LS 导出 JSON 顶层为数组）。</summary>
    public static List<AnnotationTask> DeserializeTasks(string jsonArray)
    {
        var tasks = JsonSerializer.Deserialize<List<AnnotationTask>>(jsonArray, Options);
        return tasks ?? new List<AnnotationTask>();
    }

    /// <summary>序列化任务数组。</summary>
    public static string SerializeTasks(IEnumerable<AnnotationTask> tasks) => JsonSerializer.Serialize(tasks.ToList(), Options);

    /// <summary>语义深比较（属性顺序无关），用于 round-trip 断言。</summary>
    public static bool DeepEquals(string jsonLeft, string jsonRight)
    {
        var left = JsonNode.Parse(jsonLeft);
        var right = JsonNode.Parse(jsonRight);
        return left is not null && right is not null && JsonNode.DeepEquals(left, right);
    }

    /// <summary>深度克隆任务（草稿/撤销快照用，避免共享引用）。</summary>
    public static AnnotationTask CloneTask(AnnotationTask task) => DeserializeTask(SerializeTask(task)) ?? throw new InvalidOperationException("任务克隆失败。");
}
