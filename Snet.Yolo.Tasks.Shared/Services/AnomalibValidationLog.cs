namespace Snet.Yolo.Tasks.Services;

/// <summary>保留资源键以支持刷新后切换语言的日志。</summary>
public sealed record AnomalibValidationLog(string Key, object[] Arguments);
