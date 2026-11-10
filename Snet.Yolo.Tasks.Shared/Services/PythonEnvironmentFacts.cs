namespace Snet.Yolo.Tasks.Services;

/// <summary>虚拟环境探测脚本输出的事实：PyTorch 与 CUDA 状态、关键包版本。</summary>
public sealed class PythonEnvironmentFacts
{
    /// <summary>PyTorch 版本；未安装为空。</summary>
    public string? Torch { get; init; }

    /// <summary>PyTorch 编译所用的 CUDA 版本；CPU 构建为空字符串。</summary>
    public string TorchCuda { get; init; } = string.Empty;

    /// <summary><c>torch.cuda.is_available()</c> 的结果。</summary>
    public bool CudaAvailable { get; init; }

    /// <summary>在 GPU 上成功执行了一个真实张量操作。</summary>
    public bool KernelOk { get; init; }

    /// <summary>PyTorch 可见的 GPU 名称。</summary>
    public IReadOnlyList<string> Devices { get; init; } = [];

    /// <summary>导入 PyTorch 失败时的错误说明。</summary>
    public string? TorchError { get; init; }

    /// <summary>ultralytics 版本；未安装为空。</summary>
    public string? Ultralytics { get; init; }

    /// <summary>anomalib 版本；未安装为空。</summary>
    public string? Anomalib { get; init; }

    /// <summary>onnxruntime 版本；未安装为空。</summary>
    public string? OnnxRuntime { get; init; }
}
