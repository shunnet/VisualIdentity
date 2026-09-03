namespace Snet.Yolo.Tasks.Core.Workspace;

using Snet.Yolo.Tasks.Core.Models;
using System;
using System.Collections.Generic;

/// <summary>
/// 单机工程文档（.lsp）：一个文件承载多工程及其任务/标注/配置。
/// 与 Label Studio JSON 互操作：任务部分直接复用 LS 任务数组结构。
/// </summary>
public sealed class WorkspaceDocument
{
    /// <summary>当前格式版本。</summary>
    public const int CurrentVersion = 1;

    /// <summary>格式版本。</summary>
    public int Version { get; set; } = CurrentVersion;

    /// <summary>工程列表。</summary>
    public List<WorkspaceProject> Projects { get; set; } = new();
}

/// <summary>工程：名称 + 标签配置 + 任务集合。</summary>
public sealed class WorkspaceProject
{
    /// <summary>工程 id（应用内唯一）。</summary>
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    /// <summary>工程名。</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>工程描述。</summary>
    public string? Description { get; set; }

    /// <summary>标签叠加层透明度（0.05-1，随工程保存）。</summary>
    public double OverlayOpacity { get; set; } = 0.25;

    /// <summary>Labeling Config XML（与 LS 相同格式）。</summary>
    public string LabelConfigXml { get; set; } = string.Empty;

    /// <summary>创建时间。</summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>最近保存时间。</summary>
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>任务列表（LS JSON 结构）。</summary>
    public List<AnnotationTask> Tasks { get; set; } = new();
}

/// <summary>工程文件持久化辅助。</summary>
public static class WorkspaceJson
{
    private static readonly System.Text.Json.JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase,
    };

    /// <summary>序列化为 .lsp 文本。</summary>
    public static string Serialize(WorkspaceDocument document) => System.Text.Json.JsonSerializer.Serialize(document, Options);

    /// <summary>从 .lsp 文本反序列化。</summary>
    public static WorkspaceDocument? Deserialize(string json)
        => System.Text.Json.JsonSerializer.Deserialize<WorkspaceDocument>(json, Options);
}
