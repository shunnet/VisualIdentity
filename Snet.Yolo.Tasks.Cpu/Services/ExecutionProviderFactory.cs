using YoloDotNet.ExecutionProvider.Cpu;
using YoloDotNet.Models.Interfaces;

namespace Snet.Yolo.Tasks.Services;

/// <summary>创建 CPU 推理执行提供程序。</summary>
internal sealed class ExecutionProviderFactory : IExecutionProviderFactory
{
    /// <inheritdoc />
    public IExecutionProvider Create(string modelPath) => new CpuExecutionProvider(modelPath);
}
