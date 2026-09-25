namespace Snet.Yolo.Tasks.Core.Anomalib;

using Snet.Yolo.Server.Anomalib;

/// <summary>不经过 shell 的 Anomalib 命令。</summary>
/// <param name="Executable">可执行文件绝对路径或 PATH 名称。</param>
/// <param name="ArgumentList">直接写入 ProcessStartInfo.ArgumentList 的参数。</param>
public sealed record AnomalibCommand(string Executable, IReadOnlyList<string> ArgumentList);

/// <summary>构建 Anomalib Python 流水线命令，所有动态值均保持为独立参数。</summary>
public static class AnomalibCommandBuilder
{
    /// <summary>构建训练、ONNX 导出与一致性验证的一体化命令。</summary>
    /// <param name="pythonExecutable">Anomalib 独立环境 Python。</param>
    /// <param name="scriptPath">可信内置流水线脚本。</param>
    /// <param name="configurationPath">由 .NET 生成的 JSON 配置。</param>
    /// <param name="resultPath">Python 写出的门禁结果 JSON。</param>
    /// <returns>显式可执行文件与参数列表。</returns>
    public static AnomalibCommand BuildPipeline(
        string pythonExecutable,
        string scriptPath,
        string configurationPath,
        string resultPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pythonExecutable);
        ArgumentException.ThrowIfNullOrWhiteSpace(scriptPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(configurationPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(resultPath);
        return new AnomalibCommand(pythonExecutable, [scriptPath, "--config", configurationPath, "--result", resultPath]);
    }
}
