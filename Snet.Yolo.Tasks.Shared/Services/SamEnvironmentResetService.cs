namespace Snet.Yolo.Tasks.Services;

using Snet.Yolo.Server.inference;
using Snet.Yolo.Server.sam;

/// <summary>在后台重置 SAM 的 GPU 运行环境；同一时间只允许一个重置，状态可跨页面查看。</summary>
/// <param name="installer">CUDA 运行库安装器。</param>
/// <param name="sam">共享 SAM 运行时。</param>
/// <param name="settings">全站运算硬件设置，提供验证用的 GPU 编号。</param>
/// <param name="lifetime">宿主生命周期，停止时取消重置。</param>
public sealed class SamEnvironmentResetService(CudaRuntimeInstaller installer, SamOnnxRuntime sam, OnnxRuntimeSettingsStore settings, IHostApplicationLifetime lifetime)
{
    /// <summary>当前（或最近一次）重置；没有重置过时为空。</summary>
    private SamEnvironmentResetOperation? _current;

    /// <summary>是否有重置正在进行。</summary>
    public bool IsActive => Volatile.Read(ref _current)?.IsActive == true;

    /// <summary>当前（或最近一次）重置的状态快照；没有重置过时为空。</summary>
    public TrainingEnvironmentResetStatus? GetStatus() => Volatile.Read(ref _current)?.Snapshot();

    /// <summary>启动后台重置。</summary>
    /// <returns>成功启动返回 true；已有重置在进行时返回 false。</returns>
    public bool Start()
    {
        var operation = new SamEnvironmentResetOperation();
        while (true)
        {
            var previous = Volatile.Read(ref _current);
            if (previous?.IsActive == true) { return false; }
            if (ReferenceEquals(Interlocked.CompareExchange(ref _current, operation, previous), previous)) { break; }
        }
        var gpuId = settings.Current.GpuId;
        var token = lifetime.ApplicationStopping;
        _ = Task.Run(() => operation.RunAsync(installer, sam, gpuId, token), CancellationToken.None);
        return true;
    }
}
