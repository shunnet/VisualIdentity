namespace Snet.Yolo.Tasks.Core.Training;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

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

/// <summary>一条解释器版本探测命令（可执行文件 + 完整参数）。</summary>
public sealed record PythonVersionProbe(PythonLauncher Launcher, IReadOnlyList<string> Arguments);

/// <summary>跨平台 Python 探测：解释器候选顺序、venv 能力校验与各系统安装指引。</summary>
public static class PythonDiscovery
{
    /// <summary>按优先级返回解释器候选（Windows 先 python 再 py -3；Linux/macOS 先 python3 再 python）。</summary>
    public static IReadOnlyList<PythonLauncher> Candidates(OsKind os) => os switch
    {
        OsKind.Windows => new[]
        {
            new PythonLauncher("python", Array.Empty<string>()),
            new PythonLauncher("py", new[] { "-3" }),
        },
        _ => new[]
        {
            new PythonLauncher("python3", Array.Empty<string>()),
            new PythonLauncher("python", Array.Empty<string>()),
        },
    };

    /// <summary>检测 venv 能力所用的参数（python -m venv --help）。</summary>
    public static readonly IReadOnlyList<string> VenvProbeArguments = new[] { "-m", "venv", "--help" };

    /// <summary>解释器版本探测参数。</summary>
    public static readonly IReadOnlyList<string> VersionArguments = new[] { "--version" };

    /// <summary>
    /// 版本探测的完整参数（前缀参数 + --version）。
    /// 必须走这个方法：裸跑解释器（不加 --version）会进入交互式 REPL 并永久等待标准输入，
    /// 表现为“界面一直停在检测环境、日志空白”。
    /// </summary>
    public static IReadOnlyList<string> VersionProbeArguments(PythonLauncher launcher)
        => launcher.WithArguments(VersionArguments);

    /// <summary>
    /// 按优先级返回完整的版本探测命令列表；参数构造集中在这里，
    /// 调用方直接执行即可，不会漏掉 --version。
    /// </summary>
    public static IReadOnlyList<PythonVersionProbe> VersionProbes(OsKind os)
        => Candidates(os).Select(launcher => new PythonVersionProbe(launcher, VersionProbeArguments(launcher))).ToArray();

    /// <summary>判断 --version 输出是否为 Python 3（Python 2 的 --version 同样返回 0）。</summary>
    public static bool IsPython3(string stdout, string stderr)
        => (stdout + "\n" + stderr).Contains("Python 3", StringComparison.OrdinalIgnoreCase);

    /// <summary>未安装 Python 时的中文安装指引（按系统给出可直接执行的命令）。</summary>
    public static string MissingPythonMessage(OsKind os) => os switch
    {
        OsKind.Windows => "未检测到 Python 3，请先安装：从 python.org 下载安装包并勾选 “Add python.exe to PATH”，或执行 winget install --id Python.Python.3.12。",
        OsKind.Mac => "未检测到 Python 3，请先安装：执行 brew install python，或从 python.org 下载 macOS 安装包。",
        _ => "未检测到 Python 3，请先安装：Debian/Ubuntu 执行 sudo apt install -y python3 python3-pip python3-venv；Fedora/RHEL 执行 sudo dnf install -y python3 python3-pip。",
    };

    /// <summary>Python 缺少 venv 模块时的中文安装指引。</summary>
    public static string MissingVenvMessage(OsKind os) => os switch
    {
        OsKind.Windows => "Python 缺少 venv 模块，请重新运行 python.org 官方安装包并勾选 “pip” 与 “venv” 组件（或改用完整版发行包）。",
        OsKind.Mac => "Python 缺少 venv 模块，请执行 brew install python，或改用 python.org 官方安装包。",
        _ => "Python 缺少 venv 模块，请先安装：Debian/Ubuntu 执行 sudo apt install -y python3-venv python3-pip；Fedora/RHEL 执行 sudo dnf install -y python3 python3-pip。",
    };
}

/// <summary>虚拟环境重建：python -m venv 会复用残留目录，无法修复半个安装，必须先整目录删除。</summary>
public static class VenvRebuilder
{
    /// <summary>删除残留的 venv 目录；被占用或无权限时抛出带中文说明的异常。</summary>
    public static void DeleteExisting(string venvPath)
    {
        if (string.IsNullOrWhiteSpace(venvPath) || !Directory.Exists(venvPath)) { return; }
        try
        {
            Directory.Delete(venvPath, recursive: true);
        }
        catch (IOException error)
        {
            throw new InvalidOperationException("无法删除旧的虚拟环境目录（目录可能仍被进程占用）：" + venvPath + "，请关闭正在使用该环境的 python 进程或重启应用后重试。", error);
        }
        catch (UnauthorizedAccessException error)
        {
            throw new InvalidOperationException("没有权限删除旧的虚拟环境目录：" + venvPath + "，请检查目录权限（Windows 下可以管理员身份运行）后重试。", error);
        }
    }

    /// <summary>清空并准备好 venv 目录，保证 python -m venv 从干净状态创建。</summary>
    public static void Reset(string venvPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(venvPath);
        DeleteExisting(venvPath);
        var parent = Path.GetDirectoryName(Path.TrimEndingDirectorySeparator(venvPath));
        if (!string.IsNullOrEmpty(parent)) { Directory.CreateDirectory(parent); }
    }
}
