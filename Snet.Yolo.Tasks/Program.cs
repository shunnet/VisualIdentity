using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Snet.Yolo.Server;
using Snet.Yolo.Tasks.Components;
using Snet.Yolo.Tasks.Core.Localization;
using Snet.Yolo.Tasks.Services;
using System.Globalization;
using System.Security.Claims;

var builder = WebApplication.CreateBuilder(args);

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
builder.Services.AddScoped<Snet.Yolo.Tasks.Services.ValidationState>();
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

DeleteStaleValidationUploads();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
}

app.UseAuthentication();
app.UseAuthorization();

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
}).AllowAnonymous();

app.MapPost("/auth/logout", async (HttpContext context, Microsoft.AspNetCore.Antiforgery.IAntiforgery antiforgery) =>
{
    try { await antiforgery.ValidateRequestAsync(context); }
    catch (Microsoft.AspNetCore.Antiforgery.AntiforgeryValidationException) { return Results.BadRequest(); }
    await context.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
    return Results.Redirect("/login");
}).RequireAuthorization();

// 本地上传文件服务（data/uploads/{projectId}/{fileName}）；仅用于单机工具。
app.MapGet("/uploads/{projectId}/{fileName}", (string projectId, string fileName) =>
{
    if (!IsSafePathSegment(projectId) || !IsSafePathSegment(fileName)) { return Results.BadRequest(); }
    var fileNameSafe = System.IO.Path.GetFileName(fileName);
    if (string.IsNullOrWhiteSpace(fileNameSafe)) { return Results.NotFound(); }
    var uploadsRoot = System.IO.Path.GetFullPath(System.IO.Path.Combine(AppContext.BaseDirectory, "wwwroot", "data", "uploads"));
    var file = System.IO.Path.GetFullPath(System.IO.Path.Combine(uploadsRoot, projectId, fileNameSafe));
    if (!file.StartsWith(uploadsRoot + System.IO.Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) { return Results.BadRequest(); }
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
}).RequireAuthorization();

// 验证模型列表：下载 ONNX 模型文件。
app.MapGet("/api/models/{index:int}/download", async (int index, Snet.Yolo.Server.ManageOperate manage) =>
{
    var r = await manage.QueryAsync();
    if (!r.GetDetails(out System.Collections.Generic.List<Snet.Yolo.Server.models.data.OnnxData>? list) || list is null) { return Results.Text("model not found", "text/plain", statusCode: 404); }
    var m = list.FirstOrDefault(x => x.index == index);
    if (m is null) { return Results.Text("model not found", "text/plain", statusCode: 404); }
    var path = System.IO.Path.Combine(m.path ?? "", m.name ?? "");
    if (!System.IO.File.Exists(path)) { return Results.Text("model file missing", "text/plain", statusCode: 404); }
    return Results.File(path, "application/octet-stream", m.name);
}).RequireAuthorization();

// 训练完成：下载 best.pt 模型文件。
app.MapGet("/api/train/{projectId}/best-pt", (string projectId, Snet.Yolo.Tasks.Services.TrainingService training) =>
{
    var st = training.GetStatus(projectId);
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

static void DeleteStaleValidationUploads()
{
    var directory = System.IO.Path.Combine(AppContext.BaseDirectory, "wwwroot", "data", "uploads", "val");
    if (!System.IO.Directory.Exists(directory)) { return; }
    var cutoff = DateTime.UtcNow.AddDays(-1);
    foreach (var file in System.IO.Directory.EnumerateFiles(directory))
    {
        try { if (System.IO.File.GetLastWriteTimeUtc(file) < cutoff) { System.IO.File.Delete(file); } } catch { }
    }
}
