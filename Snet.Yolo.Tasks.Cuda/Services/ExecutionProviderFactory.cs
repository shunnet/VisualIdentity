using YoloDotNet.ExecutionProvider.Cuda;
using YoloDotNet.Models.Interfaces;

namespace Snet.Yolo.Tasks.Services;

/// <summary>创建 CUDA GPU 推理执行提供程序。</summary>
internal sealed class ExecutionProviderFactory : IExecutionProviderFactory
{
    /// <inheritdoc />
    public IExecutionProvider Create(string modelPath) => new CudaExecutionProvider(modelPath, 0, null);
}
