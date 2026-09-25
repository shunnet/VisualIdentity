namespace Snet.Yolo.Tasks.Services;

using Microsoft.ML.OnnxRuntime.Tensors;
using SkiaSharp;
using Snet.Yolo.Server.Anomalib;

/// <summary>Compatibility interface for the CPU/CUDA-specific session option factories.</summary>
public interface IAnomalibSessionOptionsFactory : Snet.Yolo.Server.Anomalib.IAnomalibSessionOptionsFactory;

/// <summary>Validation-page adapter; ONNX execution and post-processing live in Server.</summary>
public sealed class AnomalibInferenceService(IAnomalibSessionOptionsFactory optionsFactory) : IDisposable
{
    private readonly AnomalibOnnxInference _inference = new(optionsFactory);

    public Task<AnomalibInferenceOutput> IdentifyAsync(RegisteredAnomalibModel model, string imagePath, CancellationToken cancellationToken = default, bool includeHeatmap = true)
    {
        ArgumentNullException.ThrowIfNull(model);
        return _inference.IdentifyAsync(model.OnnxPath, model.ManifestPath, imagePath, cancellationToken, includeHeatmap);
    }

    internal static DenseTensor<float> CreateInput(SKBitmap source, AnomalibInputContract input)
        => AnomalibOnnxInference.CreateInput(source, input);

    public void Release(string onnxPath) => _inference.Release(onnxPath);

    public void Dispose() => _inference.Dispose();
}
