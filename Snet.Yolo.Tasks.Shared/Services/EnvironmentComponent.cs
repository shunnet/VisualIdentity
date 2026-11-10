namespace Snet.Yolo.Tasks.Services;

/// <summary>设置页“环境管理”中可自检、可重置的组件。</summary>
public enum EnvironmentComponent
{
    /// <summary>标注页的 SAM：由 .NET 直接用 ONNX Runtime 推理，GPU 依赖应用私有的 CUDA 13 / cuDNN 9 运行库。</summary>
    Sam = 0,

    /// <summary>YOLO 训练环境：<c>train/yolo/.env</c> 虚拟环境（PyTorch + Ultralytics）。</summary>
    Yolo = 1,

    /// <summary>Anomalib 训练环境：<c>train/anomalib/.env</c> 虚拟环境（PyTorch + Anomalib + ONNX 工具）。</summary>
    Anomalib = 2,
}
