namespace Snet.Yolo.Tasks.Services;

/// <summary>全站 Anomalib 硬件配置及独立的 SAM 设备选择。</summary>
public interface IAnomalibSessionOptionsFactory : Snet.Yolo.Server.anomalib.IAnomalibSessionOptionsFactory
{
    /// <summary>宿主是否包含 CUDA 推理支持；不代表驱动和显卡已就绪。</summary>
    bool SupportsSamCuda { get; }
    /// <summary>创建 SAM 指定设备会话；null 为 CPU，非负编号为指定 CUDA GPU，失败不静默降级。</summary>
    /// <param name="gpuId">SAM 独立选择的 GPU 编号；空值为 CPU。</param>
    /// <returns>由调用方释放的会话选项。</returns>
    Microsoft.ML.OnnxRuntime.SessionOptions CreateSam(int? gpuId);
}
