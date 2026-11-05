using System.Net;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Snet.Yolo.Api.Model;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit;

namespace Snet.Yolo.Test;

/// <summary>YOLO API 的统一路径、HTTP 方法及 Swagger 分组回归。</summary>
public sealed class YoloApiRouteTests
{
    /// <summary>历史详情中的原图、标注图链接必须指向新路由并能取回相同图片。</summary>
    [Fact]
    public async Task HistoryDetails_ReturnsReachableYoloImageUrls()
    {
        await using var factory = new ApiRuntimeApplicationFactory();
        using var client = factory.CreateClient();
        var config = factory.Services.GetRequiredService<IOptions<ConfigModel>>().Value;
        var date = DateTime.Today.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
        var name = "route-test-" + Guid.NewGuid().ToString("N");
        var directory = Path.Combine(config.BasePath, date, "ObjectDetection");
        Directory.CreateDirectory(directory);
        var original = Path.Combine(directory, string.Format(config.OriginalImageNamingFormat, name));
        var marked = Path.Combine(directory, string.Format(config.ResultImageNamingFormat, name));
        var details = Path.Combine(directory, string.Format(config.DetailsNamingFormat, name));
        var png = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+aI1EAAAAASUVORK5CYII=");
        var cancellation = TestContext.Current.CancellationToken;
        try
        {
            await File.WriteAllBytesAsync(original, png, cancellation);
            await File.WriteAllBytesAsync(marked, png, cancellation);
            await File.WriteAllTextAsync(details, "{\"proof\":true}", cancellation);
            var result = await client.GetFromJsonAsync<JsonElement>($"/api/yolo/GetImageDetails?name={name}&type=ObjectDetection&date={date}", cancellation);
            Assert.True(result.GetProperty("Status").GetBoolean());
            var data = result.GetProperty("ResultData");
            foreach (var (key, action) in new[] { ("OriginalImage", "GetOriginalImage"), ("MarkImage", "GetMarkImage") })
            {
                var url = new Uri(data.GetProperty(key).GetString()!);
                Assert.Equal("/api/yolo/" + action, url.AbsolutePath);
                Assert.Equal(png, await client.GetByteArrayAsync(url.PathAndQuery, cancellation));
            }
        }
        finally
        {
            File.Delete(original); File.Delete(marked); File.Delete(details);
        }
    }

    /// <summary>十个公开操作必须统一到 YOLO 前缀并使用对应的 HTTP 方法。</summary>
    [Fact]
    public async Task Swagger_UsesYoloPrefixAndSemanticHttpMethods()
    {
        await using var factory = new ApiRuntimeApplicationFactory();
        using var client = factory.CreateClient();
        var document = await client.GetFromJsonAsync<JsonElement>("/swagger/v1/swagger.json", TestContext.Current.CancellationToken);
        var paths = document.GetProperty("paths");
        var actions = new Dictionary<string, string>
        {
            ["Identity"] = "post", ["IdentityDraw"] = "post", ["Add"] = "post",
            ["Update"] = "put", ["Delete"] = "delete", ["Query"] = "get", ["QueryAll"] = "get",
            ["GetOriginalImage"] = "get", ["GetMarkImage"] = "get", ["GetImageDetails"] = "get",
        };
        foreach (var (action, method) in actions)
        {
            var path = "/api/yolo/" + action;
            Assert.True(paths.TryGetProperty(path, out var operations), "缺少路由：" + path);
            Assert.True(operations.TryGetProperty(method, out var operation), "HTTP 方法错误：" + path);
            Assert.Equal("YOLO", Assert.Single(operation.GetProperty("tags").EnumerateArray()).GetString());
        }
        Assert.DoesNotContain(paths.EnumerateObject(), path => path.Name.StartsWith("/Operate/", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>只接受新约定的方法，避免旧 POST 修改/删除绕过统一接口约定。</summary>
    /// <param name="path">使用错误方法的 API 路径。</param>
    [Theory]
    [InlineData("/api/yolo/Update")]
    [InlineData("/api/yolo/Delete")]
    public async Task MutationEndpoints_RejectOldPostMethod(string path)
    {
        await using var factory = new ApiRuntimeApplicationFactory();
        using var client = factory.CreateClient();
        using var response = await client.PostAsync(path, null, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.MethodNotAllowed, response.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/Operate/QueryAll", TestContext.Current.CancellationToken)).StatusCode);
    }
}
