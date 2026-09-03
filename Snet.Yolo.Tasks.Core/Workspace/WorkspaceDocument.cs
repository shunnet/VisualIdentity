namespace Snet.Yolo.Tasks.Core.Workspace;

using Snet.Yolo.Tasks.Core.Models;
using System;
using System.Collections.Generic;

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
