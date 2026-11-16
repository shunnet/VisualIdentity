namespace Snet.Yolo.Tasks.Services;

/// <summary>上传视频触发的工具自检持有独立作用域，不能随文件导入作用域释放而取消安装。</summary>
public sealed class FfmpegPreparationService(IServiceScopeFactory scopes, ILogger<FfmpegPreparationService> logger)
{
    private readonly object _gate = new();
    private Task? _operation;

    /// <summary>复用正在执行的自动准备，不让图片上传等待工具安装。</summary>
    public void Ensure()
    {
        lock (_gate)
        {
            if (_operation is { IsCompleted: false }) { return; }
            _operation = Task.Run(async () =>
            {
                try
                {
                    using var scope = scopes.CreateScope();
                    var installer = scope.ServiceProvider.GetRequiredService<FfmpegInstaller>();
                    await installer.EnsureAsync();
                    await installer.AutomaticInstallation;
                }
                catch (Exception error) { logger.LogWarning(error, "视频工具自动准备失败，不影响文件上传"); }
            });
        }
    }
}
