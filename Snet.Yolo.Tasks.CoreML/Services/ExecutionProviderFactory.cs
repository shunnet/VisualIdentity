using YoloDotNet.ExecutionProvider.CoreML;
using YoloDotNet.Models.Interfaces;

namespace Snet.Yolo.Tasks.Services;

/// <summary>创建 Core ML 推理执行提供程序。</summary>
internal sealed class ExecutionProviderFactory : IExecutionProviderFactory
{
    /// <inheritdoc />
    public IExecutionProvider Create(string modelPath) => new CoreMLExecutionProvider(modelPath, true);
}
