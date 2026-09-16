namespace Snet.Yolo.Tasks.Services;

using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using Snet.Yolo.Tasks.Core.Training;

/// <summary>运行进程并捕获/流式读取输出（跨平台，直接调用可执行文件，不走 shell）。</summary>
public sealed class TrainingShell
{
    /// <summary>
    /// 强制子进程中的 Python 以 UTF-8 读写标准流：Windows 默认按 ANSI 代码页输出，
    /// 会让训练日志里的中文乱码（其他程序忽略这两个变量）。
    /// </summary>
    private static readonly IReadOnlyDictionary<string, string?> PythonUtf8Environment =
        new Dictionary<string, string?>(System.StringComparer.Ordinal)
        {
            ["PYTHONUTF8"] = "1",
            ["PYTHONIOENCODING"] = "utf-8",
        };

    /// <summary>以字符串参数运行（保留给既有调用方；字符串由操作系统解析）。</summary>
    public static Task<(int ExitCode, string Stdout, string Stderr)> RunAsync(string file, string args, CancellationToken ct = default)
        => RunCoreAsync(file, args, null, null, null, ct);

    /// <summary>以显式参数列表运行（推荐：路径含空格时不会错位）。</summary>
    public static Task<(int ExitCode, string Stdout, string Stderr)> RunAsync(string file, IReadOnlyList<string> arguments, CancellationToken ct = default)
        => RunCoreAsync(file, null, arguments, null, null, ct);

    /// <summary>以显式参数列表运行，可指定工作目录与子进程环境覆盖（value 为 null 表示删除该变量）。</summary>
    public static Task<(int ExitCode, string Stdout, string Stderr)> RunAsync(
        string file,
        IReadOnlyList<string> arguments,
        string? workingDirectory,
        IReadOnlyDictionary<string, string?>? environment,
        CancellationToken ct = default)
        => RunCoreAsync(file, null, arguments, workingDirectory, environment, ct);

    /// <summary>运行一条环境搭建命令。</summary>
    public static Task<(int ExitCode, string Stdout, string Stderr)> RunAsync(SetupCommand command, CancellationToken ct = default)
        => RunAsync(command.Executable, command.ArgumentList, ct);

    private static async Task<(int ExitCode, string Stdout, string Stderr)> RunCoreAsync(
        string file,
        string? rawArguments,
        IReadOnlyList<string>? arguments,
        string? workingDirectory,
        IReadOnlyDictionary<string, string?>? environment,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(file)) { return (-1, string.Empty, "未指定可执行文件"); }

        var psi = new ProcessStartInfo
        {
            FileName = file,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };
        if (arguments is not null)
        {
            foreach (var argument in arguments) { psi.ArgumentList.Add(argument); }
        }
        else
        {
            psi.Arguments = rawArguments ?? string.Empty;
        }
        if (!string.IsNullOrEmpty(workingDirectory)) { psi.WorkingDirectory = workingDirectory; }
        ApplyEnvironment(psi, environment);

        Process? proc;
        try
        {
            proc = Process.Start(psi);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception error)
        {
            return (-1, string.Empty, "无法启动进程 " + file + "：" + error.Message);
        }
        if (proc is null) { return (-1, string.Empty, "进程无法启动: " + file); }

        using (proc)
        {
            var so = new StringBuilder(); var se = new StringBuilder();
            proc.OutputDataReceived += (_, e) => { if (e.Data is not null) so.AppendLine(e.Data); };
            proc.ErrorDataReceived += (_, e) => { if (e.Data is not null) se.AppendLine(e.Data); };
            proc.BeginOutputReadLine(); proc.BeginErrorReadLine();
            try
            {
                await proc.WaitForExitAsync(ct);
            }
            catch (OperationCanceledException)
            {
                try { proc.Kill(true); } catch { }
                try { await proc.WaitForExitAsync(CancellationToken.None); } catch { }
                throw;
            }
            return (proc.ExitCode, so.ToString(), se.ToString());
        }
    }

    /// <summary>写入 UTF-8 提示与调用方给出的环境覆盖；null 值代表从子进程环境中删除该变量。</summary>
    private static void ApplyEnvironment(ProcessStartInfo psi, IReadOnlyDictionary<string, string?>? environment)
    {
        foreach (var pair in PythonUtf8Environment) { psi.Environment[pair.Key] = pair.Value; }
        if (environment is null) { return; }
        foreach (var pair in environment)
        {
            if (pair.Value is null) { psi.Environment.Remove(pair.Key); }
            else { psi.Environment[pair.Key] = pair.Value; }
        }
    }

    public static bool FileExists(string path) => File.Exists(path);
    public static bool DirectoryExists(string path) => Directory.Exists(path);
}
