namespace Snet.Yolo.Tasks.Services;

/// <summary>环境重置的只读快照；独立于训练状态，不覆盖已有指标和模型信息。</summary>
/// <param name="IsActive">后台重装是否仍在执行。</param>
/// <param name="Succeeded">安装和运行时校验是否全部通过。</param>
/// <param name="Message">最近一条进度或结果说明。</param>
/// <param name="Logs">安装日志的独立副本。</param>
public sealed record TrainingEnvironmentResetStatus(bool IsActive, bool Succeeded, string Message, IReadOnlyList<string> Logs);
