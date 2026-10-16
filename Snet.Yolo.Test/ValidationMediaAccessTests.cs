using System.Net;
using System.Net.WebSockets;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using SkiaSharp;
using Snet.Yolo.Tasks.Components;
using Xunit;

namespace Snet.Yolo.Test;

/// <summary>验证真实 Tasks 宿主的图片鉴权及交互连接过期行为。</summary>
public sealed class ValidationMediaAccessTests(ValidationMediaAccessTests.TasksFactory factory) : IClassFixture<ValidationMediaAccessTests.TasksFactory>
{
    /// <summary>YOLO、Anomalib 的原图及缩略图均可由当前登录用户加载。</summary>
    [Theory]
    [InlineData("validation", false)]
    [InlineData("validation", true)]
    [InlineData("anomalib-validation", false)]
    [InlineData("anomalib-validation", true)]
    public async Task UploadedImage_AuthenticatedOwnerReceivesDecodableImage(string scope, bool preview)
    {
        using var client = factory.CreateClient(new() { AllowAutoRedirect = false });
        await SignInAsync(client);
        var fileName = "media-test-" + Guid.NewGuid().ToString("N") + ".png";
        var directory = Path.Combine(AppContext.BaseDirectory, "wwwroot", "data", "uploads", "snet", scope);
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, fileName);
        try
        {
            using (var bitmap = new SKBitmap(32, 24))
            {
                bitmap.Erase(SKColors.CornflowerBlue);
                using var encoded = bitmap.Encode(SKEncodedImageFormat.Png, 100);
                await File.WriteAllBytesAsync(path, encoded.ToArray());
            }

            var url = preview ? $"/images/preview?scope={scope}&name={fileName}" : $"/uploads/snet/{scope}/{fileName}";
            using var response = await client.GetAsync(url);

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal(preview ? "image/jpeg" : "image/png", response.Content.Headers.ContentType?.MediaType);
            using var decoded = SKBitmap.Decode(await response.Content.ReadAsByteArrayAsync());
            Assert.NotNull(decoded);
            Assert.Equal(32, decoded.Width);
            Assert.Equal(24, decoded.Height);
        }
        finally
        {
            File.Delete(path);
            File.Delete(path + ".preview.jpg");
        }
    }

    /// <summary>未登录请求必须继续受保护，不能为解决破图而开放用户文件。</summary>
    [Theory]
    [InlineData("/uploads/snet/validation/media.png")]
    [InlineData("/uploads/snet/anomalib-validation/media.png")]
    [InlineData("/images/preview?scope=validation&name=media.png")]
    [InlineData("/images/preview?scope=anomalib-validation&name=media.png")]
    public async Task UploadedImage_AnonymousRequestRequiresLogin(string url)
    {
        using var client = factory.CreateClient(new() { AllowAutoRedirect = false });

        using var response = await client.GetAsync(url);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.StartsWith("http://localhost/login", response.Headers.Location?.ToString(), StringComparison.Ordinal);
    }

    /// <summary>登录 Cookie 失效后真实 Blazor 连接必须关闭，不能继续维持旧的已登录页面。</summary>
    [Fact]
    public async Task InteractiveConnection_ClosesWhenAuthenticationExpires()
    {
        using var client = factory.CreateClient(new() { AllowAutoRedirect = false });
        var cookie = await SignInAsync(client);
        var socketClient = factory.Server.CreateWebSocketClient();
        socketClient.ConfigureRequest = request => request.Headers.Cookie = cookie;
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        using var socket = await socketClient.ConnectAsync(new Uri("ws://localhost/_blazor"), timeout.Token);
        await socket.SendAsync("{\"protocol\":\"blazorpack\",\"version\":1}\u001e"u8.ToArray(), WebSocketMessageType.Text, true, timeout.Token);

        var buffer = new byte[1024];
        var handshake = await socket.ReceiveAsync(buffer, timeout.Token);
        Assert.NotEqual(WebSocketMessageType.Close, handshake.MessageType);
        Assert.StartsWith("{}\u001e", System.Text.Encoding.UTF8.GetString(buffer, 0, handshake.Count), StringComparison.Ordinal);
        var closed = false;
        try
        {
            while (!timeout.IsCancellationRequested)
            {
                var received = await socket.ReceiveAsync(buffer, timeout.Token);
                if (received.MessageType != WebSocketMessageType.Close) { continue; }
                closed = true;
                await socket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "", CancellationToken.None);
                break;
            }
        }
        catch (OperationCanceledException) when (timeout.IsCancellationRequested) { }

        Assert.True(closed, "登录已过期，但 Blazor 连接仍保留旧登录状态，后续图片请求会跳转到登录 HTML。");
        using var expiredRequest = await client.GetAsync("/uploads/snet/validation/media.png");
        Assert.Equal(HttpStatusCode.Redirect, expiredRequest.StatusCode);
    }

    /// <summary>通过真实登录表单取得防伪令牌及 Cookie，不绕过宿主鉴权。</summary>
    private static async Task<string> SignInAsync(HttpClient client)
    {
        var html = await client.GetStringAsync("/login");
        var token = Regex.Match(html, "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"");
        Assert.True(token.Success, "登录页必须提供防伪令牌。");
        using var response = await client.PostAsync("/auth/login", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["username"] = "snet",
            ["password"] = "123456",
            ["__RequestVerificationToken"] = WebUtility.HtmlDecode(token.Groups[1].Value),
        }));
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/projects", response.Headers.Location?.ToString());
        return response.Headers.GetValues("Set-Cookie")
            .Single(value => value.StartsWith(".AspNetCore.Cookies=", StringComparison.Ordinal)).Split(';')[0];
    }

    /// <summary>同组测试复用真实宿主；仅缩短测试 Cookie 寿命，不改业务服务与路由。</summary>
    public sealed class TasksFactory : WebApplicationFactory<App>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Development");
            builder.ConfigureServices(services => services.PostConfigure<CookieAuthenticationOptions>(
                CookieAuthenticationDefaults.AuthenticationScheme, options =>
                {
                    options.ExpireTimeSpan = TimeSpan.FromSeconds(2);
                    options.SlidingExpiration = false;
                }));
        }
    }
}
