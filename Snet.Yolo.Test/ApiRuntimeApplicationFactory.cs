using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Snet.Yolo.Server.anomalib;
using Snet.Yolo.Server.inference;

namespace Snet.Yolo.Test;

/// <summary>隔离硬件配置和模型目录的真实 API 测试宿主。</summary>
public sealed class ApiRuntimeApplicationFactory : WebApplicationFactory<Snet.Yolo.Api.Program>
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "snet-api-runtime-" + Guid.NewGuid().ToString("N"));
    /// <summary>本次测试使用的配置文件路径。</summary>
    public string SettingsPath => Path.Combine(_directory, "hardware.json");

    /// <summary>只替换持久化目录，继续通过真实控制器、JSON 绑定及原生硬件检查。</summary>
    /// <param name="builder">测试宿主配置器。</param>
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.ConfigureServices(services =>
        {
            services.RemoveAll<OnnxRuntimeSettingsStore>();
            services.AddSingleton(new OnnxRuntimeSettingsStore(SettingsPath, new() { Hardware = OnnxRuntimeHardware.Cuda }));
            services.RemoveAll<AnomalibModelRegistry>();
            services.AddSingleton(new AnomalibModelRegistry(Path.Combine(_directory, "models")));
        });
    }

    /// <summary>关闭宿主和活动会话后清理本次配置与模型。</summary>
    /// <returns>异步释放任务。</returns>
    public override async ValueTask DisposeAsync()
    {
        await base.DisposeAsync();
        if (Directory.Exists(_directory)) { Directory.Delete(_directory, recursive: true); }
    }
}
