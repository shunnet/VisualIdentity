namespace Snet.Yolo.Tasks.Core.Training;

using System.Collections.Generic;

/// <summary>一个 Python 解释器候选（可执行文件 + 固定前缀参数，如 Windows 的 py -3）。</summary>
public sealed record PythonLauncher(string Executable, IReadOnlyList<string> PrefixArguments)
{
    /// <summary>把前缀参数与给定参数拼成完整参数列表。</summary>
    public IReadOnlyList<string> WithArguments(IReadOnlyList<string> arguments)
    {
        if (PrefixArguments.Count == 0) { return arguments; }
        var list = new List<string>(PrefixArguments.Count + arguments.Count);
        list.AddRange(PrefixArguments);
        list.AddRange(arguments);
        return list;
    }
}
