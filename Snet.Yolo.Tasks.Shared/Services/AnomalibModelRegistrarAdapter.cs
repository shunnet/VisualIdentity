using Snet.Yolo.Server.Anomalib;

namespace Snet.Yolo.Tasks.Services;

/// <summary>Connects the TASKS training gate to Server model registration.</summary>
internal sealed class AnomalibModelRegistrarAdapter(AnomalibModelRegistry registry) : IAnomalibModelRegistrar
{
    public Task RegisterAsync(AnomalibTrainingArtifact artifact, CancellationToken cancellationToken)
        => registry.RegisterAsync(artifact, cancellationToken);
}
