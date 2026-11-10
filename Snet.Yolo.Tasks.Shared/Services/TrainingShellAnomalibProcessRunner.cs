namespace Snet.Yolo.Tasks.Services;

using Microsoft.Extensions.Configuration;
using Snet.Yolo.Server.anomalib;
using Snet.Yolo.Tasks.Core.Anomalib;
using Snet.Yolo.Tasks.Core.Training;

/// <summary>复用现有安全进程执行器的 Anomalib 适配器，不经过 shell。</summary>
public sealed class TrainingShellAnomalibProcessRunner : IAnomalibProcessRunner
{
    /// <summary>连续无输出超过该时长时结束进程树。</summary>
    private static readonly TimeSpan StallTimeout = TimeSpan.FromMinutes(15);

    /// <summary>仅应用到 Anomalib 子进程的代理与 CA 配置。</summary>
    private readonly IReadOnlyDictionary<string, string?>? _environment;

    /// <summary>读取与 YOLO 训练相同的代理和证书配置，不修改宿主进程环境。</summary>
    public TrainingShellAnomalibProcessRunner(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        var proxy = TrainingService.ReadProxy(configuration);
        var caBundle = TrainingService.ReadCaBundle(configuration);
        if (proxy is null && caBundle is null) { return; }
        var environment = new Dictionary<string, string?>(StringComparer.Ordinal);
        if (proxy is not null)
        {
            environment["HTTPS_PROXY"] = proxy;
            environment["HTTP_PROXY"] = proxy;
            environment["https_proxy"] = proxy;
            environment["http_proxy"] = proxy;
        }
        if (caBundle is not null)
        {
            foreach (var name in PipProxyPolicy.CertificateVariables) { environment[name] = caBundle; }
        }
        _environment = environment;
    }

    /// <summary>运行训练、导出与一致性验证 Python 流水线。</summary>
    public async Task<AnomalibProcessResult> RunAsync(
        AnomalibCommand command,
        string workingDirectory,
        Action<string>? onOutput,
        CancellationToken cancellationToken)
    {
        var result = await TrainingShell.RunStreamingAsync(
            command.Executable,
            command.ArgumentList,
            workingDirectory,
            environment: _environment,
            onOutput ?? (_ => { }),
            StallTimeout,
            cancellationToken);
        return new AnomalibProcessResult(result.ExitCode, result.Tail);
    }
}
