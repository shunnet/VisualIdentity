namespace Snet.Yolo.Tasks.Services;

/// <summary>Anomalib 视频逐帧识别的阶段。</summary>
public enum AnomalibVideoStage
{
    /// <summary>正在从视频提取帧。</summary>
    Decoding,
    /// <summary>正在逐帧执行 ONNX 推理。</summary>
    Recognizing,
    /// <summary>正在编码标注结果视频。</summary>
    Encoding,
}
