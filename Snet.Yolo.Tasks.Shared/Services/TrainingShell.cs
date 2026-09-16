namespace Snet.Yolo.Tasks.Services;

using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using Snet.Yolo.Tasks.Core.Training;

/// <summary>运行进程并捕获/流式读取输出（跨平台，直接调用可执行文件，不走 shell）。</summary>
public sealed class TrainingShell
{
    /// <summary>命令超时的退出码（区别于 -1：无法启动）。</summary>
    public const int TimeoutExitCode = -2;

    /// <summary>超时后等待进程树真正退出的上限，避免管道被孙进程占用时永久等待。</summary>
    private static readonly TimeSpan KillGracePeriod = TimeSpan.FromSeconds(5);

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
        => RunCoreAsync(file, args, null, null, null, null, ct);

    /// <summary>以显式参数列表运行（推荐：路径含空格时不会错位）；不设超时，适合训练/安装。</summary>
    public static Task<(int ExitCode, string Stdout, string Stderr)> RunAsync(string file, IReadOnlyList<string> arguments, CancellationToken ct = default)
        => RunCoreAsync(file, null, arguments, null, null, null, ct);

    /// <summary>
    /// 以显式参数列表运行并限制最长执行时间；超时后结束整棵进程树，
    /// 返回 <see cref="TimeoutExitCode"/>。环境探测一律带超时，避免命令卡死时训练永久停在“检测环境”。
    /// </summary>
    public static Task<(int ExitCode, string Stdout, string Stderr)> RunAsync(string file, IReadOnlyList<string> arguments, TimeSpan? timeout, CancellationToken ct = default)
        => RunCoreAsync(file, null, arguments, null, null, timeout, ct);

    /// <summary>以显式参数列表运行，可指定工作目录与子进程环境覆盖（value 为 null 表示删除该变量）。</summary>
    public static Task<(int ExitCode, string Stdout, string Stderr)> RunAsync(
        string file,
        IReadOnlyList<string> arguments,
        string? workingDirectory,
        IReadOnlyDictionary<string, string?>? environment,
        CancellationToken ct = default)
        => RunCoreAsync(file, null, arguments, workingDirectory, environment, null, ct);

    /// <summary>运行一条环境搭建命令。</summary>
    public static Task<(int ExitCode, string Stdout, string Stderr)> RunAsync(SetupCommand command, CancellationToken ct = default)
        => RunAsync(command.Executable, command.ArgumentList, ct);

    private static async Task<(int ExitCode, string Stdout, string Stderr)> RunCoreAsync(
        string file,
        string? rawArguments,
        IReadOnlyList<string>? arguments,
        string? workingDirectory,
        IReadOnlyDictionary<string, string?>? environment,
        TimeSpan? timeout,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(file)) { return (-1, string.Empty, "未指定可执行文件"); }

        var psi = new ProcessStartInfo
        {
            FileName = file,
            RedirectStandardInput = true,
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
            // 立刻关闭子进程的标准输入：任何“没想到会等输入”的命令（例如裸跑解释器进入 REPL、
            // 交互式确认提示）都会拿到 EOF 直接结束，而不是永久挂起、把训练卡在检测阶段。
            try { proc.StandardInput.Close(); } catch { }

            var so = new StringBuilder(); var se = new StringBuilder();
            proc.OutputDataReceived += (_, e) => { if (e.Data is not null) so.AppendLine(e.Data); };
            proc.ErrorDataReceived += (_, e) => { if (e.Data is not null) se.AppendLine(e.Data); };
            proc.BeginOutputReadLine(); proc.BeginErrorReadLine();

            using var timeoutSource = timeout is { } limit ? new CancellationTokenSource(limit) : null;
            using var linked = timeoutSource is null ? null : CancellationTokenSource.CreateLinkedTokenSource(ct, timeoutSource.Token);
            var waitToken = linked?.Token ?? ct;
            var timedOut = false;
            try
            {
                await proc.WaitForExitAsync(waitToken);
            }
            catch (OperationCanceledException)
            {
                KillTree(proc);
                await WaitForExitBoundedAsync(proc);
                // 调用方取消（用户停止 / 应用停机）仍然向外抛，只有超时才是“可继续”的失败。
                if (ct.IsCancellationRequested) { throw; }
                timedOut = true;
            }

            if (timedOut)
            {
                var seconds = (timeout?.TotalSeconds ?? 0).ToString("0.#");
                return (TimeoutExitCode, so.ToString(), "命令超时（" + seconds + " 秒）：" + CommandLine.Join(file, arguments ?? Array.Empty<string>()));
            }
            return (proc.ExitCode, so.ToString(), se.ToString());
        }
    }

    /// <summary>
    /// 判断失败结果是否属于“可执行文件根本启动不了”（例如 nvidia-smi 不存在）。
    /// 这类失败在探测多个候选路径时会重复出现，由调用方汇总成一句提示即可。
    /// </summary>
    public static bool IsStartFailure(int exitCode, string stderr)
        => exitCode == -1
           && (stderr.StartsWith("无法启动进程", StringComparison.Ordinal) || stderr.StartsWith("进程无法启动", StringComparison.Ordinal));

    /// <summary>结束整棵进程树；进程已退出时忽略。</summary>
    private static void KillTree(Process proc)
    {
        try { proc.Kill(entireProcessTree: true); } catch { }
    }

    /// <summary>有上限地等待进程退出，避免信号/管道被继承时永久阻塞。</summary>
    private static async Task WaitForExitBoundedAsync(Process proc)
    {
        try { await Task.WhenAny(proc.WaitForExitAsync(CancellationToken.None), Task.Delay(KillGracePeriod)); }
        catch { }
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
