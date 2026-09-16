namespace Snet.Yolo.Tasks.Core.Training;

using System.Collections.Generic;
using System.Linq;

/// <summary>命令行文本工具：仅用于日志展示与兼容调用，执行一律使用显式参数列表。</summary>
public static class CommandLine
{
    /// <summary>
    /// 单个参数在需要时加引号（仅影响展示文本，不影响实际执行）。
    /// 形如 key=value 的参数只给 value 加引号，保持既有日志格式 data="C:\My Data\data.yaml"。
    /// </summary>
    public static string Quote(string value)
    {
        var separator = value.IndexOf('=');
        if (separator >= 0 && separator + 1 < value.Length)
        {
            return value[..(separator + 1)] + QuoteValue(value[(separator + 1)..]);
        }
        return QuoteValue(value);
    }

    private static string QuoteValue(string value)
    {
        if (value.Length == 0) { return "\"\""; }
        if (value.IndexOfAny(new[] { ' ', '\t', '"' }) < 0) { return value; }
        return "\"" + value.Replace("\"", "\\\"") + "\"";
    }

    /// <summary>把参数列表拼成可读的一行（不含可执行文件）。</summary>
    public static string JoinArguments(IReadOnlyList<string> arguments)
    {
        if (arguments.Count == 0) { return string.Empty; }
        return string.Join(' ', arguments.Select(Quote));
    }

    /// <summary>把可执行文件与参数列表拼成可读的一行命令。</summary>
    public static string Join(string executable, IReadOnlyList<string> arguments)
    {
        var text = JoinArguments(arguments);
        return text.Length == 0 ? Quote(executable) : Quote(executable) + " " + text;
    }
}
