using Snet.Yolo.Tasks.Core.Training;
using Snet.Yolo.Tasks.Services;
using System.Diagnostics;
using Xunit;

namespace Snet.Yolo.Test;

/// <summary>
/// 训练子进程的“不会永久卡住”契约。
/// 背景：Linux 上训练曾停在“检测环境”且日志空白——探测命令没有超时、也没有日志，
/// 一旦某条命令不返回就永远等下去，停止按钮也救不回来。
/// </summary>
public sealed class TrainingShellTests
{
    [Fact]
    public void RuntimeProbeScript_DoesNotImportUltralytics()
    {
        var script = TorchRuntimeProbe.Script(OsKind.Linux);

        // ultralytics 在导入时会做联网版本检查/字体下载，代理不通时会长时间挂起；
        // 探测只需要一个版本号，必须走 importlib.metadata。
        Assert.DoesNotContain("import torch, ultralytics", script, StringComparison.Ordinal);
        Assert.Contains("importlib.metadata", script, StringComparison.Ordinal);
        Assert.Contains("torch.cuda.is_available()", script, StringComparison.Ordinal);
    }

    [Fact]
    public void RuntimeProbeScript_UsesMpsOnMac()
    {
        var script = TorchRuntimeProbe.Script(OsKind.Mac);

        Assert.Contains("torch.backends.mps.is_available()", script, StringComparison.Ordinal);
        Assert.DoesNotContain("cuda", script, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(-1, "无法启动进程 nvidia-smi：系统找不到指定的文件。", true)]
    [InlineData(-1, "进程无法启动: nvidia-smi", true)]
    [InlineData(-1, "命令超时（8 秒）：nvidia-smi", false)]
    [InlineData(1, "no such file", false)]
    [InlineData(0, "", false)]
    public void StartFailure_IsDistinguishedFromOtherFailures(int exitCode, string stderr, bool expected)
    {
        Assert.Equal(expected, TrainingShell.IsStartFailure(exitCode, stderr));
    }

    [Fact]
    public async Task RunAsync_ReturnsOutputOfAQuickCommand()
    {
        var (file, arguments) = QuickCommand();

        var result = await TrainingShell.RunAsync(file, arguments, TimeSpan.FromSeconds(30), CancellationToken.None);

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("snet-probe", result.Stdout, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RunAsync_KillsCommandThatExceedsItsTimeoutInsteadOfHangingForever()
    {
        var (file, arguments) = SleepingCommand();
        var stopwatch = Stopwatch.StartNew();

        var result = await TrainingShell.RunAsync(file, arguments, TimeSpan.FromSeconds(2), CancellationToken.None);
        stopwatch.Stop();

        Assert.Equal(TrainingShell.TimeoutExitCode, result.ExitCode);
        Assert.Contains("超时", result.Stderr, StringComparison.Ordinal);
        Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(25), $"超时未生效，耗时 {stopwatch.Elapsed.TotalSeconds:0.#} 秒");
    }

    [Fact]
    public async Task RunAsync_StillThrowsWhenTheCallerCancels()
    {
        var (file, arguments) = SleepingCommand();
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(600));

        // 用户点“停止训练”必须抛取消（而不是被当成探测超时继续往下跑）。
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => TrainingShell.RunAsync(file, arguments, TimeSpan.FromSeconds(30), cancellation.Token));
    }

    /// <summary>输出一行可断言的快速命令。</summary>
    private static (string File, string[] Arguments) QuickCommand() => OperatingSystem.IsWindows()
        ? ("cmd", new[] { "/c", "echo snet-probe" })
        : ("/bin/echo", new[] { "snet-probe" });

    /// <summary>明显长于超时时间的命令（Windows 用 ping 计时，类 Unix 用 sleep）。</summary>
    private static (string File, string[] Arguments) SleepingCommand() => OperatingSystem.IsWindows()
        ? ("cmd", new[] { "/c", "ping -n 31 127.0.0.1" })
        : ("/bin/sleep", new[] { "30" });
}
