using Snet.Yolo.Server.inference;
using YoloDotNet.Models.Interfaces;

namespace Snet.Yolo.Tasks.Services;

/// <summary>按全站快照创建 YOLO 执行提供程序，只使用宿主的一套原生运行库。</summary>
/// <param name="settings">已保存的全站验证设备。</param>
/// <param name="installer">CUDA 运行库准备服务。</param>
internal sealed class ExecutionProviderFactory(OnnxRuntimeSettingsStore settings, CudaRuntimeInstaller installer) : IExecutionProviderFactory
{
    /// <summary>CPU 不探测或下载 CUDA；CUDA 检查实际选择的 GPU。</summary>
    /// <param name="progress">环境准备进度。</param>
    /// <param name="cancellationToken">取消探测和安装。</param>
    /// <returns>所选硬件的准备结果，不改变已保存配置。</returns>
    public async Task<HardwarePreparationResult> EnsureHardwareReadyAsync(Action<string>? progress = null, CancellationToken cancellationToken = default)
    {
        var snapshot = settings.Current;
        if (snapshot.Hardware == OnnxRuntimeHardware.Cpu) { return HardwarePreparationResult.Cpu; }
        var result = OnnxSessionOptions.SupportsCuda
            ? await installer.EnsureAsync(progress, cancellationToken, snapshot.GpuId)
            : new HardwarePreparationResult(true, false, false, "当前平台原生运行库不支持 CUDA，请在设置中选择 CPU。");
        return result;
    }

    /// <summary>一次创建使用一个配置快照；CUDA 失败时不静默更换设备。</summary>
    /// <param name="modelPath">ONNX 模型绝对路径。</param>
    /// <returns>由识别调用方释放的执行提供程序。</returns>
    public IExecutionProvider Create(string modelPath)
    {
        var snapshot = settings.Current;
        if (snapshot.Hardware == OnnxRuntimeHardware.Cpu) { return new CpuExecutionProvider(modelPath); }
        var reason = CudaRuntimeLibraries.TryPrepare(AppContext.BaseDirectory);
        if (reason is not null) { throw new InvalidOperationException("所选 CUDA 推理设备不可用：" + reason); }
        return new CudaExecutionProvider(modelPath, snapshot.GpuId);
    }
}
