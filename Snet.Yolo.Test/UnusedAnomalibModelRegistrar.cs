using Snet.Yolo.Server.anomalib;
using Snet.Yolo.Tasks.Services;

namespace Snet.Yolo.Test;

/// <summary>不会被调用的模型注册器：用于不应注册模型的训练测试，被调用即说明流程走错了分支。</summary>
internal sealed class UnusedAnomalibModelRegistrar : IAnomalibModelRegistrar
{
    /// <summary>被调用即抛出异常。</summary>
    public Task RegisterAsync(AnomalibTrainingArtifact artifact, CancellationToken cancellationToken)
        => throw new InvalidOperationException("训练不应该注册模型。");
}
