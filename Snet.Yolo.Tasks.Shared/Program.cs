using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.Extensions.FileProviders;
using Snet.Log;
using Snet.Yolo.Server;
using Snet.Yolo.Server.Anomalib;
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
// 大图/弱网下浏览器可能一时发不出心跳，放宽服务端的"等客户端"上限（默认 30 秒）。
// 注意：KeepAliveInterval 必须保持默认的 15 秒 —— Blazor 客户端的服务器超时默认 30 秒，
// 官方要求"客户端超时 ≥ 2 × 心跳间隔"；把它调到 30 秒会让客户端每隔几十秒误判掉线并弹出重连提示。
builder.Services.AddSignalR(options =>
{
    options.ClientTimeoutInterval = TimeSpan.FromMinutes(2);
    options.KeepAliveInterval = TimeSpan.FromSeconds(15);
});
// 加载大图时的 JS 互操作给足时间（默认 1 分钟，这里再宽一点）
builder.Services.Configure<Microsoft.AspNetCore.Components.Server.CircuitOptions>(options =>
{
    options.JSInteropDefaultCallTimeout = TimeSpan.FromMinutes(2);
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
// 验证页预览图（原图不压缩：上传后按需/后台生成一张小预览供页面显示）
var validationPreviewOptions = new Snet.Yolo.Tasks.Services.ImagePreviewOptions();
builder.Configuration.GetSection("Images:Preview").Bind(validationPreviewOptions);
builder.Services.AddSingleton(validationPreviewOptions);
builder.Services.AddSingleton<Snet.Yolo.Tasks.Services.ImagePreviewStore>();
builder.Services.AddScoped<UploadCenter>();
builder.Services.AddSingleton<TrainingService>();
builder.Services.AddSingleton<IAnomalibProcessRunner, TrainingShellAnomalibProcessRunner>();
builder.Services.AddSingleton<AnomalibModelRegistry>();
builder.Services.AddSingleton<IAnomalibModelRegistrar, AnomalibModelRegistrarAdapter>();
builder.Services.AddSingleton<AnomalibTrainingService>();
builder.Services.AddSingleton<AnomalibWorkflowService>();
builder.Services.AddScoped<Snet.Yolo.Tasks.Services.IAnomalibSessionOptionsFactory, AnomalibSessionOptionsFactory>();
builder.Services.AddScoped<AnomalibInferenceService>();
builder.Services.AddScoped<AnomalibVideoService>();
builder.Services.AddSingleton<CudaRuntimeInstaller>();
builder.Services.Configure<MediaToolOptions>(builder.Configuration.GetSection(MediaToolOptions.SectionName));
builder.Services.AddSingleton<MediaToolSettingsStore>();
builder.Services.AddSingleton<MediaToolResolver>();
// 中文字体：视频结果帧由 SkiaSharp 绘制，默认字体没有中文字形（会画成方框），必须显式提供。
builder.Services.AddSingleton<ICjkFontProvider, MediaFontResolver>();
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
        list?.FirstOrDefault(candidate => UserNameNormalizer.Normalize(candidate.username) == UserNameNormalizer.Normalize(username) && candidate.active == 1) is not { } user)
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

// 通用图片预览：命中小图直接返回；否则当场生成一次（每图只做一次），失败则回退原图。
// scope 为 validation（验证页）或工程 id（项目详情/标注页的缩略图）。
// 注意：这里**不能**注入 ValidationService / CurrentUserContext —— 它们依赖 Blazor 电路的
// AuthenticationStateProvider，在最小 API 请求里会直接抛异常（表现为页面所有图片 500 → 破图）。
app.MapGet("/images/preview", async (HttpContext context, string scope, string name, Snet.Yolo.Tasks.Services.ImagePreviewStore previews, CancellationToken cancellationToken) =>
{
    var currentOwner = context.User.Identity?.Name;
    if (string.IsNullOrWhiteSpace(currentOwner)) { return Results.Forbid(); }
    if (!IsSafePathSegment(scope)) { return Results.BadRequest(); }
    var fileName = System.IO.Path.GetFileName(name);
    if (string.IsNullOrWhiteSpace(fileName) || fileName != name) { return Results.BadRequest(); }
    var directory = System.IO.Path.Combine(AppContext.BaseDirectory, "wwwroot", "data", "uploads", UserStoragePath.Segment(currentOwner), scope);
    var original = System.IO.Path.GetFullPath(System.IO.Path.Combine(directory, fileName));
    if (!original.StartsWith(System.IO.Path.GetFullPath(directory) + System.IO.Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) { return Results.BadRequest(); }
    if (!System.IO.File.Exists(original)) { return Results.NotFound(); }
    var preview = await previews.GetOrCreateAsync(original, cancellationToken);
    return preview is null
        ? Results.File(original, MediaContentType(original), enableRangeProcessing: true)
        : Results.File(preview, "image/jpeg", enableRangeProcessing: true);
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

// Anomalib 模型必须连同解析清单一起下载，且只能读取当前用户通过一致性校验的产物。
app.MapGet("/api/anomalib/models/{projectId}/{runId}/download", async (HttpContext context, string projectId, string runId, AnomalibModelRegistry registry) =>
{
    var owner = context.User.Identity?.Name;
    if (string.IsNullOrWhiteSpace(owner)) { return Results.Unauthorized(); }
    if (!IsSafePathSegment(projectId) || !IsSafePathSegment(runId)) { return Results.BadRequest(); }
    var model = await registry.FindAsync(owner, projectId, runId, context.RequestAborted);
    if (model is null) { return Results.NotFound(); }
    var package = await AnomalibModelRegistry.OpenPackageDownloadAsync(model, context.RequestAborted);
    return Results.File(package, "application/zip", $"{runId}.zip");
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
// 发布目录中存在脚本但静态资源清单失配时，仅为内置 JS 提供物理文件回退；不得暴露 wwwroot/data 中的用户上传文件。
var scriptDirectory = Path.Combine(AppContext.BaseDirectory, "wwwroot", "js");
if (Directory.Exists(scriptDirectory))
{
    app.UseStaticFiles(new StaticFileOptions
    {
        FileProvider = new PhysicalFileProvider(scriptDirectory),
        RequestPath = "/js",
    });
}
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
