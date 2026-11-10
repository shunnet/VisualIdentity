namespace Snet.Yolo.Tasks.Services;

using Snet.Yolo.Server.anomalib;
using Snet.Yolo.Tasks.Core.Anomalib;

/// <summary>执行 Anomalib 流水线进程的抽象，便于验证参数安全、取消与失败分支。</summary>
public interface IAnomalibProcessRunner
{
    /// <summary>使用显式参数列表执行命令，并流式返回日志。</summary>
    /// <param name="command">可信可执行文件与参数列表。</param>
    /// <param name="workingDirectory">子进程工作目录。</param>
    /// <param name="onOutput">可选日志回调。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>进程退出码与输出尾部。</returns>
    Task<AnomalibProcessResult> RunAsync(
        AnomalibCommand command,
        string workingDirectory,
        Action<string>? onOutput,
        CancellationToken cancellationToken);
}
