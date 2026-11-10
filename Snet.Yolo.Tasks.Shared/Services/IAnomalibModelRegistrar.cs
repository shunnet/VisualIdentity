namespace Snet.Yolo.Tasks.Services;

using Snet.Yolo.Server.anomalib;

/// <summary>只有模型通过一致性门禁后才会调用的注册边界。</summary>
public interface IAnomalibModelRegistrar
{
    /// <summary>注册已经通过门禁且文件完整的 Anomalib 模型。</summary>
    /// <param name="artifact">可信训练产物。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    Task RegisterAsync(AnomalibTrainingArtifact artifact, CancellationToken cancellationToken);
}
