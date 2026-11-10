namespace Snet.Yolo.Tasks.Core.Config;

/// <summary>配置问题条目。</summary>
public sealed record ConfigIssue(ConfigIssueSeverity Severity, string Code, string Message);
