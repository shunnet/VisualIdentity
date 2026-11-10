namespace Snet.Yolo.Tasks.Core.Training;

using System.Collections.Generic;

/// <summary>一条环境搭建命令：可执行文件 + 参数列表（避免命令行字符串往返解析导致路径带空格时出错）。</summary>
/// <param name="Executable">可执行文件（可为空，表示仅提示的 Info 步骤）。</param>
/// <param name="ArgumentList">显式参数列表，直接写入 ProcessStartInfo.ArgumentList。</param>
/// <param name="IsNetwork">是否为依赖网络的 pip 安装步骤（失败时按代理重试阶梯重试）。</param>
public sealed record SetupCommand(string Executable, IReadOnlyList<string> ArgumentList, bool IsNetwork = false)
{
    /// <summary>仅用于日志展示的命令行文本。</summary>
    public string Arguments => CommandLine.JoinArguments(ArgumentList);
}
