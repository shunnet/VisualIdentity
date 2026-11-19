using Snet.Yolo.Server.models;
using Snet.Yolo.Tasks.Core.Anomalib;
using Snet.Yolo.Tasks.Core.Training;

namespace Snet.Yolo.Tasks.Services;

/// <summary>重建并校验应用私有训练环境，网络安装沿用统一的代理重试策略。</summary>
internal static class TrainingEnvironmentInstaller
{
    /// <summary>保留原虚拟环境直到新环境安装及运算校验成功。调用方持有共享环境的互斥权。</summary>
    internal static async Task InstallAsync(ProjectKind kind, string venv, string python, IReadOnlyList<SetupStep> steps, string root, string channel,
        int[] selectedGpus, string? proxy, string? caBundle, Action<string> log, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var previous = venv + ".previous";
        if (!Directory.Exists(previous) && Directory.Exists(venv))
        {
            Directory.Move(venv, previous);
        }

        try
        {
            VenvRebuilder.Reset(venv);
            foreach (var step in steps)
            {
                await RunStepAsync(step, root, proxy, caBundle, log, TimeSpan.FromMinutes(15), token);
            }

            var script = kind == ProjectKind.Anomalib ? AnomalibEnvironmentPlanner.EnvironmentProbeScript
                : "import os; os.environ['YOLO_CONFIG_DIR'] = " + System.Text.Json.JsonSerializer.Serialize(Path.Combine(venv, ".ultralytics"))
                    + "; " + TorchRuntimeProbe.EnvironmentScript + "; import ultralytics; print(ultralytics.__version__)";
            if (channel != "cpu")
            {
                var gpuIds = string.Join(',', selectedGpus.Length == 0 ? [0] : selectedGpus);
                script += "; [torch.ones(1, device='cuda:' + str(i)).sum().item() for i in [" + gpuIds + "]]";
            }
            var verification = await TrainingShell.RunAsync(python, ["-c", script], TimeSpan.FromMinutes(2), token);
            log(verification.Stdout);
            if (verification.ExitCode != 0 || kind == ProjectKind.Anomalib && !AnomalibEnvironmentPlanner.IsEnvironmentReady(verification.Stdout, channel))
            {
                throw new InvalidOperationException("训练环境安装后校验失败：" + verification.Stderr);
            }
            token.ThrowIfCancellationRequested();
        }
        catch
        {
            VenvRebuilder.DeleteExisting(venv);
            if (Directory.Exists(previous))
            {
                Directory.Move(previous, venv);
                log("已恢复安装前的训练环境。");
            }
            throw;
        }

        // 新环境已验证，清理备份失败时仍可使用新环境。
        try { VenvRebuilder.DeleteExisting(previous); }
        catch (InvalidOperationException error) { log("旧环境备份清理失败：" + error.Message); }
    }

    /// <summary>流式执行安装步骤，失败时按代理策略重试并保留根因。</summary>
    internal static async Task RunStepAsync(SetupStep step, string root, string? proxy, string? caBundle,
        Action<string> log, TimeSpan stallTimeout, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        log(step.Description);
        for (var attempt = 1; attempt <= (step.IsNetwork ? PipProxyPolicy.MaxAttempts : 1); attempt++)
        {
            var arguments = step.IsNetwork ? PipProxyPolicy.BuildArguments(step.Command.ArgumentList, attempt, proxy) : step.Command.ArgumentList;
            var result = await TrainingShell.RunStreamingAsync(step.Command.Executable, arguments, root,
                PipProxyPolicy.BuildEnvironmentOverrides(attempt, caBundle), log, stallTimeout, token);
            if (result.ExitCode == 0) { return; }
            if (!step.IsNetwork || attempt == PipProxyPolicy.MaxAttempts)
            {
                throw new InvalidOperationException(step.Description + "失败：" + result.Tail);
            }
            log(PipProxyPolicy.DescribeAttempt(attempt + 1));
        }
    }
}
