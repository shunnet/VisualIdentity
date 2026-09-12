namespace Snet.Yolo.Tasks.Services;

using System.Diagnostics;
using System.Text;

/// <summary>运行进程并捕获/流式读取输出（跨平台，直接调用可执行文件，不走 shell）。</summary>
public sealed class TrainingShell
{
    public static async Task<(int ExitCode, string Stdout, string Stderr)> RunAsync(string file, string args, CancellationToken ct = default)
    {
        var psi = new ProcessStartInfo(file, args) { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8 };
        using var proc = Process.Start(psi);
        if (proc is null) { return (-1, string.Empty, "进程无法启动: " + file); }
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

    public static bool FileExists(string path) => File.Exists(path);
    public static bool DirectoryExists(string path) => Directory.Exists(path);
}
