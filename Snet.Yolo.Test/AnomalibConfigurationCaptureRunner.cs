using System.Text.Json;
using Snet.Yolo.Server.anomalib;
using Snet.Yolo.Tasks.Core.Anomalib;
using Snet.Yolo.Tasks.Services;

namespace Snet.Yolo.Test;

/// <summary>捕获实际写给 Python 的配置，并让门禁按预期失败，避免测试注册虚构模型。</summary>
internal sealed class AnomalibConfigurationCaptureRunner : IAnomalibProcessRunner, IAnomalibModelRegistrar
{
    /// <summary>本次进程收到的 JSON 配置副本。</summary>
    public JsonElement Configuration { get; private set; }
    /// <summary>服务尝试注册模型的次数。</summary>
    public int RegistrationCount { get; private set; }

    /// <summary>在临时运行目录清理前读取真实配置。</summary>
    /// <param name="command">包含配置和结果文件路径的实际命令。</param>
    /// <param name="workingDirectory">隔离的测试运行目录。</param>
    /// <param name="onOutput">可选日志接收器。</param>
    /// <param name="cancellationToken">测试取消令牌。</param>
    /// <returns>预期失败的进程结果，不执行真实模型训练。</returns>
    public async Task<AnomalibProcessResult> RunAsync(AnomalibCommand command, string workingDirectory, Action<string>? onOutput, CancellationToken cancellationToken)
    {
        using var json = JsonDocument.Parse(await File.ReadAllTextAsync(command.ArgumentList[2], cancellationToken));
        Configuration = json.RootElement.Clone();
        await File.WriteAllTextAsync(command.ArgumentList[4], """{"status":"failed","sampleCount":0,"errors":["configuration capture"]}""", cancellationToken);
        return new AnomalibProcessResult(2, "configuration capture");
    }

    /// <summary>记录注册边界是否被错误触发。</summary>
    /// <param name="artifact">服务尝试注册的产物。</param>
    /// <param name="cancellationToken">测试取消令牌。</param>
    /// <returns>已经完成的记录任务。</returns>
    public Task RegisterAsync(AnomalibTrainingArtifact artifact, CancellationToken cancellationToken)
    {
        RegistrationCount++;
        return Task.CompletedTask;
    }
}
