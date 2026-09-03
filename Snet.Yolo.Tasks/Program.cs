using Snet.Yolo.Tasks.Components;
using Snet.Yolo.Tasks.Services;
using Snet.Yolo.Server;
using Snet.Yolo.Tasks.Core;
using Snet.Yolo.Tasks.Core.Localization;
using System.Globalization;

var builder = WebApplication.CreateBuilder(args);

// Blazor InteractiveServer 组件服务。
builder.WebHost.ConfigureKestrel(o => o.Limits.MaxRequestBodySize = 1L * 1024 * 1024 * 1024);
builder.Services.Configure<Microsoft.AspNetCore.Http.Features.FormOptions>(o => o.MultipartBodyLengthLimit = 1L * 1024 * 1024 * 1024);
builder.Services.Configure<Microsoft.AspNetCore.SignalR.HubOptions>(o => o.MaximumReceiveMessageSize = 1L * 1024 * 1024 * 1024);

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents(options =>
    {
        // 仅开发环境暴露详细电路错误，便于联调。
        if (builder.Environment.IsDevelopment())
        {
            options.DetailedErrors = true;
        }
    });

// 语言管理器：Scoped（每个信号连接电路独立一份）。
builder.Services.AddScoped<LanguageManager>();

builder.Services.AddScoped<WorkspaceService>();
builder.Services.AddScoped<ToastService>();
builder.Services.AddSingleton<TrainingService>();

builder.Services.AddSingleton(Snet.Yolo.Server.UserOperate.Instance(Snet.Yolo.Server.handler.PublicHandler.DefaultSN));
builder.Services.AddSingleton<ManageOperate>(Snet.Yolo.Server.ManageOperate.Instance(Snet.Yolo.Server.handler.PublicHandler.DefaultSN));
builder.Services.AddSingleton<Snet.Yolo.Server.ProjectOperate>(Snet.Yolo.Server.ProjectOperate.Instance(Snet.Yolo.Server.handler.PublicHandler.DefaultSN));
builder.Services.AddSingleton<Snet.Yolo.Server.ProjectTaskOperate>(Snet.Yolo.Server.ProjectTaskOperate.Instance(Snet.Yolo.Server.handler.PublicHandler.DefaultSN));
builder.Services.AddScoped<ValidationService>();
builder.Services.AddScoped<UserService>();
builder.Services.AddScoped<AuthService>();
builder.Services.AddSingleton<SystemMetrics>();
builder.Services.AddSignalR();

// 应用默认语言：中文（zh-CN）；运行时可切换，偏好持久化在浏览器 localStorage。
CultureInfo.DefaultThreadCurrentCulture = CultureInfo.GetCultureInfo(LanguageManager.DefaultLanguageCode);
CultureInfo.DefaultThreadCurrentUICulture = CultureInfo.GetCultureInfo(LanguageManager.DefaultLanguageCode);

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
}

// 本地上传文件服务（data/uploads/{projectId}/{fileName}）；仅用于单机工具。
app.MapGet("/uploads/{projectId}/{fileName}", (string projectId, string fileName) =>
{
    var fileNameSafe = System.IO.Path.GetFileName(fileName);
    if (string.IsNullOrWhiteSpace(fileNameSafe)) { return Results.NotFound(); }
    var file = System.IO.Path.Combine(AppContext.BaseDirectory, "data", "uploads", projectId, fileNameSafe);
    if (!System.IO.File.Exists(file)) { return Results.NotFound(); }
    var ext = System.IO.Path.GetExtension(file).ToLowerInvariant();
    var contentType = ext switch
    {
        ".jpg" => "image/jpeg",
        ".jpeg" => "image/jpeg",
        ".png" => "image/png",
        ".gif" => "image/gif",
        ".webp" => "image/webp",
        ".bmp" => "image/bmp",
        _ => "application/octet-stream",
    };
    return Results.File(file, contentType);
});

app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
app.UseHttpsRedirection();
app.UseAntiforgery();
app.MapStaticAssets();
app.MapHub<TrainingHub>("/hubs/training");
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();
