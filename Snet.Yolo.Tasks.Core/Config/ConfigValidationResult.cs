namespace Snet.Yolo.Tasks.Core.Config;

using System;
using System.Collections.Generic;
using System.Linq;

/// <summary>校验结果。</summary>
public sealed class ConfigValidationResult
{
    /// <summary>问题列表。</summary>
    public List<ConfigIssue> Issues { get; } = new();

    /// <summary>是否可用（无 Error 级问题）。</summary>
    public bool IsValid => Issues.All(issue => issue.Severity != ConfigIssueSeverity.Error);

    /// <summary>Error 级问题数量。</summary>
    public int ErrorCount => Issues.Count(issue => issue.Severity == ConfigIssueSeverity.Error);
}
