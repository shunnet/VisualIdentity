using YoloDotNet.ExecutionProvider.DirectML;
using YoloDotNet.Models.Interfaces;

namespace Snet.Yolo.Tasks.Services;

/// <summary>创建 DirectML GPU 推理执行提供程序。</summary>
internal sealed class ExecutionProviderFactory : IExecutionProviderFactory
{
    /// <inheritdoc />
    public IExecutionProvider Create(string modelPath) => new DirectMLExecutionProvider(modelPath, 0);
}
