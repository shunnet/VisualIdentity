using Microsoft.ML.OnnxRuntime;

namespace Snet.Yolo.Server.Anomalib;

/// <summary>Creates provider-specific ONNX Runtime options for Anomalib inference.</summary>
public interface IAnomalibSessionOptionsFactory
{
    /// <summary>Creates a new options instance owned by the inference service.</summary>
    SessionOptions Create();
}
