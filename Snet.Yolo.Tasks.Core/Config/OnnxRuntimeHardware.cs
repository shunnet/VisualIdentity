namespace Snet.Yolo.Tasks.Core.Config;

/// <summary>全站验证使用的 ONNX Runtime 执行硬件；不控制 SAM 或 Python 训练。</summary>
public enum OnnxRuntimeHardware
{
    /// <summary>CPU 执行提供程序。</summary>
    Cpu,
    /// <summary>指定单张 NVIDIA GPU 的 CUDA 执行提供程序。</summary>
    Cuda,
}
