using Snet.Yolo.Server.anomalib;
using Snet.Yolo.Tasks.Core.Anomalib;
using Snet.Yolo.Tasks.Services;

namespace Snet.Yolo.Test;

/// <summary>不会被调用的进程运行器：用于在启动流水线之前就结束的训练测试，被调用即说明流程走错了分支。</summary>
internal sealed class UnusedAnomalibProcessRunner : IAnomalibProcessRunner
{
    /// <summary>被调用即抛出异常。</summary>
    public Task<AnomalibProcessResult> RunAsync(AnomalibCommand command, string workingDirectory, Action<string>? onOutput, CancellationToken cancellationToken)
        => throw new InvalidOperationException("训练不应该启动流水线进程。");
}
