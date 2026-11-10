namespace Snet.Yolo.Tasks.Core.Training;

using System;
using System.Collections.Generic;
using System.Linq;

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
