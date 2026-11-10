namespace Snet.Yolo.Tasks.Core.Anomalib;

/// <summary>不经过 shell 的 Anomalib 命令。</summary>
/// <param name="Executable">可执行文件绝对路径或 PATH 名称。</param>
/// <param name="ArgumentList">直接写入 ProcessStartInfo.ArgumentList 的参数。</param>
public sealed record AnomalibCommand(string Executable, IReadOnlyList<string> ArgumentList);
