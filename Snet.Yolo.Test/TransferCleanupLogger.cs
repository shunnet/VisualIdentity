using Microsoft.Extensions.Logging;
using Snet.Yolo.Tasks.Services;

namespace Snet.Yolo.Test;

/// <summary>观察真实暂存清理日志，在不向生产代码添加测试钩子的情况下记录清理时的公开状态。</summary>
internal sealed class TransferCleanupLogger(Action<string> observe) : ILogger<PersistentUploadService>
{
    /// <summary>测试日志不持有作用域资源。</summary>
    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

    /// <summary>保留清理失败等所有级别的诊断。</summary>
    public bool IsEnabled(LogLevel logLevel) => true;

    /// <summary>同步观察日志发生时的状态，不延迟后台任务。</summary>
    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        => observe(formatter(state, exception));
}
