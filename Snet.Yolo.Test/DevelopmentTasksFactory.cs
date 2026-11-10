using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Snet.Yolo.Tasks.Components;

namespace Snet.Yolo.Test;

/// <summary>以开发环境启动真实的 Tasks 宿主，用于验证依赖注入容器与路由等启动期行为。</summary>
public sealed class DevelopmentTasksFactory : WebApplicationFactory<App>
{
    /// <summary>使用与本地调试一致的开发环境，不改动任何服务注册。</summary>
    /// <param name="builder">宿主构建器。</param>
    protected override void ConfigureWebHost(IWebHostBuilder builder) => builder.UseEnvironment("Development");
}
