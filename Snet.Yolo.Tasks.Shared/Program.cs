using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.Extensions.FileProviders;
using Snet.Log;
using Snet.Yolo.Server;
using Snet.Yolo.Server.anomalib;
using Snet.Yolo.Server.inference;
using Snet.Yolo.Tasks.Components;
using Snet.Yolo.Tasks.Core.History;
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

// 大文件上传上限。导入数据集 ZIP 走的是「先选文件、再按流读取」这条路：
//   1) 真正的闸门是 UploadCenter.MaxArchiveBytes —— 它同时是 InputFile.OpenReadStream 的 maxAllowedSize，
//      超过它浏览器端的文件流会直接被拒绝（这就是 3 GB 的 Roboflow 数据集以前传不进来的原因）；
//   2) Kestrel 与表单上限同样放大到同一量级，避免将来换成表单/直传（HTTP 请求体）时又在 1 GiB 处静默失败。
// 说明：交互式服务端组件读取文件走 SignalR 流式互操作，不经过请求体，所以这两项只是防御性对齐。
builder.WebHost.ConfigureKestrel(o => o.Limits.MaxRequestBodySize = UploadCenter.MaxArchiveBytes);
builder.Services.Configure<Microsoft.AspNetCore.Http.Features.FormOptions>(o => o.MultipartBodyLengthLimit = UploadCenter.MaxArchiveBytes);
// InputFile 以小块流式传输：服务端（RemoteJSDataStream，.NET 10 与 11 的实现相同）按 min(最大消息长度, 50 KiB) - 512 字节决定每个数据块的大小，
// 1 MiB 已经让数据块取到 50 KiB 的上限（且 50 KiB 的 base64 编码后仍远小于 1 MiB），
// 因此不需要为“更大的 ZIP”再去抬高单条 SignalR 消息上限 —— 抬高只会削弱防护，不会更快。
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
builder.Services.AddScoped<ThemeManager>();
builder.Services.AddSingleton(provider => new AppearanceSettingsStore(logger: provider.GetRequiredService<ILogger<AppearanceSettingsStore>>()));

builder.Services.AddScoped<WorkspaceService>();
builder.Services.AddScoped<CurrentUserContext>();
builder.Services.AddScoped<ToastService>();
// 上传中心：每个电路一份。上传任务由它持有，页面切换不会中断上传，切回后仍能读到进度。
// 验证页预览图（原图不压缩：上传后按需/后台生成一张小预览供页面显示）
var validationPreviewOptions = new Snet.Yolo.Tasks.Services.ImagePreviewOptions();
builder.Configuration.GetSection("Images:Preview").Bind(validationPreviewOptions);
builder.Services.AddSingleton(validationPreviewOptions);
builder.Services.AddSingleton<Snet.Yolo.Tasks.Services.ImagePreviewStore>();
// 启动后在后台把历史上传图片缺失的预览/缩略图补齐，页面真正打开时通常已就绪。
builder.Services.AddHostedService<Snet.Yolo.Tasks.Services.ImagePreviewWarmupService>();
builder.Services.AddScoped<UploadCenter>();
builder.Services.AddSingleton<TrainingHistoryStore>();
builder.Services.AddSingleton<TrainingService>();
builder.Services.AddSingleton<IAnomalibProcessRunner, TrainingShellAnomalibProcessRunner>();
builder.Services.AddSingleton<AnomalibModelRegistry>();
builder.Services.AddSingleton<IAnomalibModelRegistrar, AnomalibModelRegistrarAdapter>();
builder.Services.AddSingleton<AnomalibTrainingService>();
builder.Services.AddSingleton<AnomalibWorkflowService>();
builder.Services.AddSingleton<TrainingHistoryService>();
builder.Services.AddHostedService(provider => provider.GetRequiredService<AnomalibWorkflowService>());
builder.Services.AddScoped<Snet.Yolo.Tasks.Services.IAnomalibSessionOptionsFactory, AnomalibSessionOptionsFactory>();
builder.Services.AddSingleton(new Snet.Yolo.Server.sam.SamModelStore());
builder.Services.AddSingleton(provider =>
{
    using var configurationScope = provider.CreateScope();
    var supportsCuda = configurationScope.ServiceProvider.GetRequiredService<Snet.Yolo.Tasks.Services.IAnomalibSessionOptionsFactory>().SupportsSamCuda;
    return new Snet.Yolo.Server.sam.SamOnnxRuntime(
        provider.GetRequiredService<Snet.Yolo.Server.sam.SamModelStore>(), gpuId =>
        {
            // 复用 CPU/CUDA 宿主的 ONNX 硬件配置，不经过 API，也不持有 Scoped 服务。
            using var scope = provider.GetRequiredService<IServiceScopeFactory>().CreateScope();
            return scope.ServiceProvider.GetRequiredService<Snet.Yolo.Tasks.Services.IAnomalibSessionOptionsFactory>().CreateSam(gpuId);
        }, supportsCuda);
});
builder.Services.AddScoped(provider => new AnomalibOnnxInference(provider.GetRequiredService<Snet.Yolo.Tasks.Services.IAnomalibSessionOptionsFactory>()));
builder.Services.AddScoped(provider => new YoloValidationService(
    provider.GetRequiredService<ManageOperate>(),
    path => provider.GetRequiredService<IExecutionProviderFactory>().Create(path)));
builder.Services.AddScoped<JointValidationService>();
builder.Services.AddScoped<AnomalibVideoService>();
builder.Services.AddSingleton<CudaRuntimeInstaller>();
builder.Services.AddSingleton(provider => new OnnxRuntimeSettingsStore(
    Path.Combine(AppContext.BaseDirectory, "wwwroot", "data", "settings", "onnx-runtime.json"),
    logger: provider.GetRequiredService<ILogger<OnnxRuntimeSettingsStore>>()));
builder.Services.AddScoped<OnnxRuntimeSettingsService>();
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
builder.Services.AddSingleton<AnomalibValidationState>();
builder.Services.AddSingleton<JointValidationState>();
builder.Services.AddSingleton<VideoRecognitionQueue>();
builder.Services.AddHostedService(provider => provider.GetRequiredService<VideoRecognitionQueue>());
builder.Services.AddSingleton<SystemMetrics>();
// 设置页的环境自检与 SAM 环境重置：状态需要跨页面、跨电路保留，因此注册为单例。
builder.Services.AddSingleton<EnvironmentDiagnosticsService>();
builder.Services.AddSingleton<SamEnvironmentResetService>();

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
    return ServeCachedFile(context, file, MediaContentType(file));
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
    return System.IO.File.Exists(file) ? ServeCachedFile(context, file, MediaContentType(file)) : Results.NotFound();
}).RequireAuthorization();

// 通用图片预览：命中小图直接返回；否则当场生成一次（每图只做一次）。预览图失败时回退原图，缩略图失败时返回 404。
// scope 为 validation / anomalib-validation（验证页）或工程 id（项目详情/标注页的缩略图）。
// 注意：这里**不能**注入 ValidationService / CurrentUserContext —— 它们依赖 Blazor 电路的
// AuthenticationStateProvider，在最小 API 请求里会直接抛异常（表现为页面所有图片 500 → 破图）。
// size=thumb 取列表/网格用的小缩略图（最长边 ≤ 360），缺省取主视图用的预览图（最长边 ≤ 1600）。
// 响应带 Cache-Control 与 ETag：返回页面、翻页、切换工程时浏览器直接用本地缓存，不再逐张请求。
app.MapGet("/images/preview", async (HttpContext context, string scope, string name, string? size, Snet.Yolo.Tasks.Services.ImagePreviewStore previews, CancellationToken cancellationToken) =>
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
    var variant = string.Equals(size, "thumb", StringComparison.OrdinalIgnoreCase)
        ? Snet.Yolo.Tasks.Services.ImageVariant.Thumbnail
        : Snet.Yolo.Tasks.Services.ImageVariant.Preview;
    var derived = await previews.GetOrCreateAsync(original, variant, cancellationToken);
    // 缩略图生成失败时不回退到几十 MB 的原图：一张缩略图占满浏览器的 6 个连接会拖慢整个页面，
    // 返回 404 让页面显示占位即可（原图仍可在大图查看器里打开）。预览图失败仍回退原图，保证主视图能看。
    if (derived is null && variant == Snet.Yolo.Tasks.Services.ImageVariant.Thumbnail && previews.Enabled) { return Results.NotFound(); }
    return derived is null
        ? ServeCachedFile(context, original, MediaContentType(original))
        : ServeCachedFile(context, derived, derived == original ? MediaContentType(original) : "image/jpeg");
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

// 完整导出的训练产物允许当前所有者下载；校准未通过也不会自动加入验证模型列表。
app.MapGet("/api/anomalib/training/{projectId}/{runId}/download", async (HttpContext context, string projectId, string runId, AnomalibModelRegistry registry) =>
{
    var owner = context.User.Identity?.Name;
    if (string.IsNullOrWhiteSpace(owner)) { return Results.Unauthorized(); }
    if (!IsSafePathSegment(projectId) || !IsSafePathSegment(runId)) { return Results.BadRequest(); }
    var package = await registry.OpenTrainingPackageDownloadAsync(owner, projectId, runId, context.RequestAborted);
    if (package is null) { return Results.NotFound(); }
    context.Response.Headers.CacheControl = "private, no-store";
    return Results.File(package, "application/zip", $"{runId}.zip");
}).RequireAuthorization();

// 报告独立下载，模型 ZIP 保持现有的 ONNX 与清单两文件格式。
app.MapGet("/api/anomalib/training/{projectId}/{runId}/report", async (HttpContext context, string projectId, string runId, AnomalibModelRegistry registry) =>
{
    var owner = context.User.Identity?.Name;
    if (string.IsNullOrWhiteSpace(owner)) { return Results.Unauthorized(); }
    if (!IsSafePathSegment(projectId) || !IsSafePathSegment(runId)) { return Results.BadRequest(); }
    var report = await registry.OpenTrainingReportDownloadAsync(owner, projectId, runId, context.RequestAborted);
    if (report is null) { return Results.NotFound(); }
    context.Response.Headers.CacheControl = "private, no-store";
    return Results.File(report, "application/json", $"{runId}-calibration.json");
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

// 训练指标页的模型导出：YOLO 为 pt / onnx，Anomalib 为 package / onnx / report；只能读取当前用户自己的训练记录。
app.MapGet("/api/train-history/{kind}/{projectId}/{runId}/{format}", async (HttpContext context, string kind, string projectId, string runId, string format, TrainingHistoryService history) =>
{
    var owner = context.User.Identity?.Name;
    if (string.IsNullOrWhiteSpace(owner)) { return Results.Unauthorized(); }
    if (kind is not ("yolo" or "anomalib") || !IsSafePathSegment(projectId) || !IsSafePathSegment(runId) || !IsSafePathSegment(format)) { return Results.BadRequest(); }
    var download = await history.OpenDownloadAsync(kind == "anomalib" ? Snet.Yolo.Server.models.ProjectKind.Anomalib : Snet.Yolo.Server.models.ProjectKind.Yolo,
        owner, projectId, runId, format, context.RequestAborted);
    if (download is null) { return Results.NotFound(); }
    context.Response.Headers.CacheControl = "private, no-store";
    return Results.File(download.Stream, download.ContentType, download.FileName);
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
    .AddInteractiveServerRenderMode()
    .Add(endpoint =>
    {
        // Cookie 过期时关闭交互连接，重连会重新验证身份；否则页面仍显示已登录，
        // 而 YOLO / Anomalib 的受保护图片请求已返回登录 HTML，导致整页破图。
        var connectionOptions = endpoint.Metadata.OfType<HttpConnectionDispatcherOptions>().FirstOrDefault();
        if (connectionOptions is not null) { connectionOptions.CloseOnAuthenticationExpiration = true; }
    });

app.Run();

// 上传图片与派生图的文件名是随机 GUID，同名文件内容不会变化：允许浏览器私有缓存一天，过期后凭 ETag 复验（304，不重传）。
// 此前这些响应没有任何缓存头，从 anomalib-project 回到验证页、翻页、重进工程都会把所有图片重新拉一遍。
// 只对图片这样处理：识别结果视频的文件名可能复用，缓存会让用户看到上一次的结果；视频只带 ETag/Last-Modified 复验。
static IResult ServeCachedFile(HttpContext context, string path, string contentType)
{
    var info = new System.IO.FileInfo(path);
    context.Response.Headers.CacheControl = contentType.StartsWith("image/", StringComparison.Ordinal) ? "private, max-age=86400" : "private, no-cache";
    var tag = new Microsoft.Net.Http.Headers.EntityTagHeaderValue($"\"{info.Length:x}-{info.LastWriteTimeUtc.Ticks:x}\"");
    return Results.File(path, contentType, enableRangeProcessing: true, lastModified: info.LastWriteTimeUtc, entityTag: tag);
}

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
