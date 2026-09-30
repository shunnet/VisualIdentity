namespace Snet.Yolo.Tasks.Services;

/// <summary>CPU/CUDA 版本提供的 Anomalib ONNX 会话配置。</summary>
public interface IAnomalibSessionOptionsFactory : Snet.Yolo.Server.anomalib.IAnomalibSessionOptionsFactory
{
    /// <summary>宿主是否包含 CUDA 推理支持；不代表驱动和显卡已就绪。</summary>
    bool SupportsSamCuda { get; }
    /// <summary>创建 SAM 指定设备会话；null 为 CPU，非负编号为指定 CUDA GPU，失败不静默降级。</summary>
    Microsoft.ML.OnnxRuntime.SessionOptions CreateSam(int? gpuId);
}
