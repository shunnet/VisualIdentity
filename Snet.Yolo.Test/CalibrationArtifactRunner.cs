using System.Security.Cryptography;
using System.Text.Json;
using Snet.Yolo.Tasks.Core.Anomalib;
using Snet.Yolo.Tasks.Services;
using Snet.Yolo.Server.anomalib;

namespace Snet.Yolo.Test;

/// <summary>生成真实可加载的小型 ONNX 文件，模拟训练进程的导出与校准报告。</summary>
internal sealed class CalibrationArtifactRunner(string report, int exitCode = 0) : IAnomalibProcessRunner, IAnomalibModelRegistrar
{
    /// <summary>注册器被调用的次数。</summary>
    public int RegistrationCount { get; private set; }

    /// <summary>四输出常量 ONNX，用于下载和手动导入的契约测试，不代表业务模型。</summary>
    private const string ModelData = "CAg6uAMKMRIKcHJlZF9zY29yZSIIQ29uc3RhbnQqGQoFdmFsdWUqDQgBEAEiBM3MzD1CAXOgAQQKLhIKcHJlZF9sYWJlbCIIQ29uc3RhbnQqFgoFdmFsdWUqCggBEAkqAQBCAWygAQQKchILYW5vbWFseV9tYXAiCENvbnN0YW50KlkKBXZhbHVlKk0IAQgECAQQASJAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAEIBbaABBApAEglwcmVkX21hc2siCENvbnN0YW50KikKBXZhbHVlKh0IAQgECAQQCSoQAAAAAAAAAAAAAAAAAAAAAEIBcKABBBIEdGVzdFofCgVpbnB1dBIWChQIARIQCgIIAQoCCAMKAgggCgIIIGIYCgpwcmVkX3Njb3JlEgoKCAgBEgQKAggBYhgKCnByZWRfbGFiZWwSCgoICAkSBAoCCAFiIQoLYW5vbWFseV9tYXASEgoQCAESDAoCCAEKAggECgIIBGIfCglwcmVkX21hc2sSEgoQCAkSDAoCCAEKAggECgIIBEIECgAQDQ==";

    /// <summary>写出独立产物和指定报告，模拟进程结束。</summary>
    /// <param name="command">包含真实配置与结果文件路径的命令。</param>
    /// <param name="workingDirectory">测试运行目录。</param>
    /// <param name="onOutput">阶段日志接收器。</param>
    /// <param name="cancellationToken">测试取消令牌。</param>
    /// <returns>指定的退出码。</returns>
    public async Task<AnomalibProcessResult> RunAsync(AnomalibCommand command, string workingDirectory, Action<string>? onOutput, CancellationToken cancellationToken)
    {
        using var config = JsonDocument.Parse(await File.ReadAllTextAsync(command.ArgumentList[2], cancellationToken));
        var root = config.RootElement.GetProperty("artifactRoot").GetString()!;
        Directory.CreateDirectory(root);
        var bytes = Convert.FromBase64String(ModelData);
        await File.WriteAllBytesAsync(Path.Combine(root, "model.onnx"), bytes, cancellationToken);
        await File.WriteAllBytesAsync(Path.Combine(root, "model.ckpt"), [1, 2, 3], cancellationToken);
        var digest = Convert.ToHexStringLower(SHA256.HashData(bytes));
        await File.WriteAllTextAsync(Path.Combine(root, "model.manifest.json"), """
            {"schemaVersion":"1.0","family":"anomalib","anomalibVersion":"2.6.2","algorithm":"padim","modelSha256":"MODEL_SHA",
             "input":{"name":"input","elementType":"float32","layout":"nchw","colorSpace":"rgb","width":32,"height":32,"resizeMode":"stretch","valueRange":"zeroToOne","normalizationEmbedded":true},
             "outputs":{"predictionScore":"pred_score","predictionLabel":"pred_label","anomalyMap":"anomaly_map","predictionMask":"pred_mask"},
             "postProcessing":{"threshold":0.5,"thresholdSource":"manual"}}
            """.Replace("MODEL_SHA", digest, StringComparison.Ordinal), cancellationToken);
        onOutput?.Invoke("VISUALIDENTITY_PHASE:parity");
        await File.WriteAllTextAsync(command.ArgumentList[4], report, cancellationToken);
        return new AnomalibProcessResult(exitCode, "calibration fixture");
    }

    /// <summary>记录自动注册是否被触发。</summary>
    /// <param name="artifact">本次训练产物。</param>
    /// <param name="cancellationToken">测试取消令牌。</param>
    /// <returns>已经完成的任务。</returns>
    public Task RegisterAsync(AnomalibTrainingArtifact artifact, CancellationToken cancellationToken)
    {
        RegistrationCount++;
        return Task.CompletedTask;
    }
}
