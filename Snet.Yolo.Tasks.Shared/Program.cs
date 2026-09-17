using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Snet.Log;
using Snet.Yolo.Server;
using Snet.Yolo.Tasks.Components;
using Snet.Yolo.Tasks.Core.Localization;
using Snet.Yolo.Tasks.Services;
using System.Globalization;
using System.Security.Claims;
using System.Threading.RateLimiting;


LogHelper.Set(new() { ConsoleOut = false });

var builder = WebApplication.CreateBuilder(new WebApplicationOptions
{
    Args = args,
    ContentRootPath = AppContext.BaseDirectory,
});

// Blazor InteractiveServer 组件服务。
builder.WebHost.ConfigureKestrel(o => o.Limits.MaxRequestBodySize = 1L * 1024 * 1024 * 1024);
builder.Services.Configure<Microsoft.AspNetCore.Http.Features.FormOptions>(o => o.MultipartBodyLengthLimit = 1L * 1024 * 1024 * 1024);
// InputFile 以小块流式传输，不需要允许单条超大 SignalR 消息。
builder.Services.Configure<Microsoft.AspNetCore.SignalR.HubOptions>(o => o.MaximumReceiveMessageSize = 1024 * 1024);

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents(options =>
    {
        // 仅开发环境暴露详细电路错误，便于联调。
        if (builder.Environment.IsDevelopment())
        {
            options.DetailedErrors = true;
        }
    });
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.LoginPath = "/login";
        options.AccessDeniedPath = "/login";
        options.Cookie.HttpOnly = true;
        options.Cookie.SameSite = SameSiteMode.Strict;
        options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
        options.ExpireTimeSpan = TimeSpan.FromHours(8);
        options.SlidingExpiration = true;
    });
builder.Services.AddAuthorization();
builder.Services.AddCascadingAuthenticationState();
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddPolicy("login", context => RateLimitPartition.GetFixedWindowLimiter(
        context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = 10,
            Window = TimeSpan.FromMinutes(1),
            QueueLimit = 0,
            AutoReplenishment = true,
        }));
});

// 语言管理器：Scoped（每个信号连接电路独立一份）。
builder.Services.AddScoped<LanguageManager>();

builder.Services.AddScoped<WorkspaceService>();
builder.Services.AddScoped<CurrentUserContext>();
builder.Services.AddScoped<ToastService>();
// 上传中心：每个电路一份。上传任务由它持有，页面切换不会中断上传，切回后仍能读到进度。
builder.Services.AddScoped<UploadCenter>();
builder.Services.AddSingleton<TrainingService>();
builder.Services.Configure<MediaToolOptions>(builder.Configuration.GetSection(MediaToolOptions.SectionName));
builder.Services.AddSingleton<MediaToolSettingsStore>();
builder.Services.AddSingleton<MediaToolResolver>();
builder.Services.AddSingleton<IFfmpegDownloader, HttpFfmpegDownloader>();
builder.Services.AddSingleton<ISystemCommandRunner, SystemCommandRunner>();
// FFmpeg 自检/安装：与上传中心同为 Scoped（每个电路一份），页面切换期间安装继续进行。
builder.Services.AddScoped<FfmpegInstaller>();
builder.Services.AddSingleton<ValidationFileLifetime>();

builder.Services.AddSingleton(Snet.Yolo.Server.UserOperate.Instance(Snet.Yolo.Server.handler.PublicHandler.DefaultSN));
builder.Services.AddSingleton<ManageOperate>(Snet.Yolo.Server.ManageOperate.Instance(Snet.Yolo.Server.handler.PublicHandler.DefaultSN));
builder.Services.AddSingleton<Snet.Yolo.Server.ProjectOperate>(Snet.Yolo.Server.ProjectOperate.Instance(Snet.Yolo.Server.handler.PublicHandler.DefaultSN));
builder.Services.AddSingleton<Snet.Yolo.Server.ProjectTaskOperate>(Snet.Yolo.Server.ProjectTaskOperate.Instance(Snet.Yolo.Server.handler.PublicHandler.DefaultSN));
builder.Services.AddScoped<ValidationService>();
builder.Services.AddSingleton<IExecutionProviderFactory, ExecutionProviderFactory>();
builder.Services.AddScoped<UserService>();
builder.Services.AddSingleton<Snet.Yolo.Tasks.Services.ValidationState>();
builder.Services.AddSingleton<VideoRecognitionQueue>();
builder.Services.AddHostedService(provider => provider.GetRequiredService<VideoRecognitionQueue>());
builder.Services.AddSingleton<SystemMetrics>();
builder.Services.AddSignalR();

// 应用默认语言：中文（zh-CN）；运行时可切换，偏好持久化在浏览器 localStorage。
CultureInfo.DefaultThreadCurrentCulture = CultureInfo.GetCultureInfo(LanguageManager.DefaultLanguageCode);
CultureInfo.DefaultThreadCurrentUICulture = CultureInfo.GetCultureInfo(LanguageManager.DefaultLanguageCode);

var app = builder.Build();

var userInitialization = await app.Services.GetRequiredService<UserOperate>().QueryAsync();
if (!userInitialization.Status)
{
    throw new InvalidOperationException(userInitialization.Message ?? "User store initialization failed.");
}
var projectInitialization = await app.Services.GetRequiredService<ProjectOperate>().InitializeAsync();
if (!projectInitialization.Status)
{
    throw new InvalidOperationException(projectInitialization.Message ?? "Project store initialization failed.");
}
var modelInitialization = await app.Services.GetRequiredService<ManageOperate>().InitializeAsync();
if (!modelInitialization.Status)
{
    throw new InvalidOperationException(modelInitialization.Message ?? "Model store initialization failed.");
}

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
}

app.UseAuthentication();
app.UseAuthorization();
app.UseRateLimiter();

app.MapPost("/auth/login", async (HttpContext context, UserOperate users, Microsoft.AspNetCore.Antiforgery.IAntiforgery antiforgery) =>
{
    try { await antiforgery.ValidateRequestAsync(context); }
    catch (Microsoft.AspNetCore.Antiforgery.AntiforgeryValidationException) { return Results.BadRequest(); }
    var form = await context.Request.ReadFormAsync(context.RequestAborted);
    var username = form["username"].ToString().Trim();
    var password = form["password"].ToString();
    var verified = await users.VerifyAsync(username, password, context.RequestAborted);
    if (!verified.Status) { return Results.Redirect("/login?error=1"); }

    var query = await users.QueryAsync(context.RequestAborted);
    if (!query.GetDetails(out List<Snet.Yolo.Server.models.data.UserData>? list) ||
        list?.FirstOrDefault(candidate => candidate.username == username && candidate.active == 1) is not { } user)
    {
        return Results.Redirect("/login?error=1");
    }

    var identity = new ClaimsIdentity(
        new[] { new Claim(ClaimTypes.Name, user.username), new Claim(ClaimTypes.Role, user.role) },
        CookieAuthenticationDefaults.AuthenticationScheme);
    await context.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, new ClaimsPrincipal(identity));
    return Results.Redirect("/projects");
}).AllowAnonymous().RequireRateLimiting("login");

app.MapPost("/auth/logout", async (HttpContext context, Microsoft.AspNetCore.Antiforgery.IAntiforgery antiforgery) =>
{
    try { await antiforgery.ValidateRequestAsync(context); }
    catch (Microsoft.AspNetCore.Antiforgery.AntiforgeryValidationException) { return Results.BadRequest(); }
    await context.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
    return Results.Redirect("/login");
}).RequireAuthorization();

// 用户隔离的本地上传文件服务（data/uploads/{owner}/{scope}/{fileName}）。
app.MapGet("/uploads/{owner}/{scope}/{fileName}", (HttpContext context, string owner, string scope, string fileName) =>
{
    var currentOwner = context.User.Identity?.Name;
    if (string.IsNullOrWhiteSpace(currentOwner) || owner != UserStoragePath.Segment(currentOwner)) { return Results.Forbid(); }
    if (!IsSafePathSegment(owner) || !IsSafePathSegment(scope) || !IsSafePathSegment(fileName)) { return Results.BadRequest(); }
    var fileNameSafe = System.IO.Path.GetFileName(fileName);
    if (string.IsNullOrWhiteSpace(fileNameSafe)) { return Results.NotFound(); }
    var uploadsRoot = System.IO.Path.GetFullPath(System.IO.Path.Combine(AppContext.BaseDirectory, "wwwroot", "data", "uploads"));
    var file = System.IO.Path.GetFullPath(System.IO.Path.Combine(uploadsRoot, owner, scope, fileNameSafe));
    if (!file.StartsWith(uploadsRoot + System.IO.Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) { return Results.BadRequest(); }
    if (!System.IO.File.Exists(file)) { return Results.NotFound(); }
    return Results.File(file, MediaContentType(file), enableRangeProcessing: true);
}).RequireAuthorization();

// 兼容历史 snet 数据；新上传不再使用该路径。
app.MapGet("/uploads/{projectId}/{fileName}", (HttpContext context, string projectId, string fileName) =>
{
    if (!string.Equals(context.User.Identity?.Name, "snet", StringComparison.OrdinalIgnoreCase)) { return Results.Forbid(); }
    if (!IsSafePathSegment(projectId) || !IsSafePathSegment(fileName)) { return Results.BadRequest(); }
    var uploadsRoot = System.IO.Path.GetFullPath(System.IO.Path.Combine(AppContext.BaseDirectory, "wwwroot", "data", "uploads"));
    var file = System.IO.Path.GetFullPath(System.IO.Path.Combine(uploadsRoot, projectId, System.IO.Path.GetFileName(fileName)));
    if (!System.IO.File.Exists(file)) { file = System.IO.Path.GetFullPath(System.IO.Path.Combine(uploadsRoot, "snet", projectId, System.IO.Path.GetFileName(fileName))); }
    if (!file.StartsWith(uploadsRoot + System.IO.Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) { return Results.BadRequest(); }
    return System.IO.File.Exists(file) ? Results.File(file, MediaContentType(file), enableRangeProcessing: true) : Results.NotFound();
}).RequireAuthorization();

// 验证模型列表：下载 ONNX 模型文件。
app.MapGet("/api/models/{index:int}/download", async (HttpContext context, int index, Snet.Yolo.Server.ManageOperate manage) =>
{
    var owner = context.User.Identity?.Name;
    if (string.IsNullOrWhiteSpace(owner)) { return Results.Unauthorized(); }
    var r = await manage.QueryAsync(owner, index, context.RequestAborted);
    if (!r.GetDetails(out System.Collections.Generic.List<Snet.Yolo.Server.models.data.OnnxData>? list) || list is null) { return Results.Text("model not found", "text/plain", statusCode: 404); }
    var m = list.FirstOrDefault(x => x.index == index);
    if (m is null) { return Results.Text("model not found", "text/plain", statusCode: 404); }
    var path = System.IO.Path.Combine(m.path ?? "", m.name ?? "");
    if (!System.IO.File.Exists(path)) { return Results.Text("model file missing", "text/plain", statusCode: 404); }
    return Results.File(path, "application/octet-stream", m.name);
}).RequireAuthorization();

// 训练完成：下载 best.pt 模型文件。
app.MapGet("/api/train/{projectId}/best-pt", (HttpContext context, string projectId, Snet.Yolo.Tasks.Services.TrainingService training) =>
{
    var owner = context.User.Identity?.Name;
    if (string.IsNullOrWhiteSpace(owner)) { return Results.Unauthorized(); }
    var st = training.GetStatus(owner, projectId);
    if (st is null || string.IsNullOrEmpty(st.BestModelPath) || !System.IO.File.Exists(st.BestModelPath)) { return Results.Text("best.pt not found", "text/plain", statusCode: 404); }
    return Results.File(st.BestModelPath, "application/octet-stream", System.IO.Path.GetFileName(st.BestModelPath));
}).RequireAuthorization();

app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
app.UseHttpsRedirection();
app.UseAntiforgery();
app.MapStaticAssets();
app.MapHub<TrainingHub>("/hubs/training").RequireAuthorization();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();

static bool IsSafePathSegment(string value)
    => !string.IsNullOrWhiteSpace(value)
       && value is not "." and not ".."
       && value.All(character => char.IsLetterOrDigit(character) || character is '-' or '_' or '.');

static string MediaContentType(string file) => System.IO.Path.GetExtension(file).ToLowerInvariant() switch
{
    ".jpg" or ".jpeg" => "image/jpeg",
    ".png" => "image/png",
    ".gif" => "image/gif",
    ".webp" => "image/webp",
    ".bmp" => "image/bmp",
    ".mp4" or ".m4v" => "video/mp4",
    ".webm" => "video/webm",
    ".mov" => "video/quicktime",
    ".avi" => "video/x-msvideo",
    ".mkv" => "video/x-matroska",
    _ => "application/octet-stream",
};
