using Snet.Yolo.Server.inference;

namespace Snet.Yolo.Tasks.Services;

/// <summary>在服务端检查管理员权限和指定 GPU 后保存全站验证设备。</summary>
/// <param name="user">当前电路的可信登录身份。</param>
/// <param name="store">全站配置存储。</param>
/// <param name="installer">共享 CUDA 运行库准备服务。</param>
public sealed class OnnxRuntimeSettingsService(CurrentUserContext user, OnnxRuntimeSettingsStore store, CudaRuntimeInstaller installer)
{
    /// <summary>仅管理员可保存；GPU 准备失败、取消或写入失败均保留旧配置。</summary>
    /// <param name="settings">待保存的不可变硬件配置。</param>
    /// <param name="progress">CUDA 环境准备进度回调。</param>
    /// <param name="cancellationToken">取消保存和环境准备。</param>
    public async Task SaveAsync(OnnxRuntimeSettings settings, Action<string>? progress = null, CancellationToken cancellationToken = default)
    {
        if (!await user.IsAdministratorAsync()) { throw new UnauthorizedAccessException("只有管理员可以修改全站推理硬件。"); }
        settings.Validate();
        cancellationToken.ThrowIfCancellationRequested();
        if (settings.Hardware == OnnxRuntimeHardware.Cuda)
        {
            if (!OnnxSessionOptions.SupportsCuda) { throw new NotSupportedException("当前平台原生包不支持 CUDA，请使用 CPU。"); }
            var preparation = await installer.EnsureAsync(progress, cancellationToken, settings.GpuId);
            if (!preparation.GpuReady) { throw new InvalidOperationException(preparation.Message); }
        }
        cancellationToken.ThrowIfCancellationRequested();
        store.Save(settings);
    }
}
