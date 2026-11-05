using Microsoft.Extensions.DependencyInjection;
using Microsoft.ML.OnnxRuntime;
using SkiaSharp;
using Snet.Yolo.Api.Services;
using Snet.Yolo.Server.inference;
using System.Net;
using System.IO.Compression;
using System.Net.Http.Json;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Xunit;

namespace Snet.Yolo.Test;

/// <summary>统一 API 的默认设备、参数绑定、持久化、失败回滚及 Swagger 回归。</summary>
public sealed class ApiRuntimeHardwareTests
{
    /// <summary>真实 HTTP 响应必须包含可供识别调用使用的模型编号，不能被只读属性忽略配置清空。</summary>
    [Fact]
    public async Task AnomalibImport_ReturnsModelIdentifiersInHttpResponse()
    {
        await using var factory = new ApiRuntimeApplicationFactory();
        using var client = factory.CreateClient();
        var bytes = ApiModelFixtures.AnomalibOnnx;
        var manifest = $$"""
            {"schemaVersion":"1.0","family":"anomalib","anomalibVersion":"2.6.2","algorithm":"padim","modelSha256":"{{Convert.ToHexStringLower(SHA256.HashData(bytes))}}",
             "input":{"name":"image","elementType":"float32","layout":"nchw","colorSpace":"rgb","width":32,"height":32,"resizeMode":"stretch","valueRange":"zeroToOne","normalizationEmbedded":true},
             "outputs":{"predictionScore":"pred_score","predictionLabel":"pred_label","anomalyMap":"anomaly_map","predictionMask":"pred_mask"},"postProcessing":{"threshold":0.5,"thresholdSource":"manual"} }
            """;
        using var package = new MemoryStream();
        using (var zip = new ZipArchive(package, ZipArchiveMode.Create, leaveOpen: true))
        {
            using (var file = zip.CreateEntry("model.onnx").Open()) { file.Write(bytes); }
            using var json = new StreamWriter(zip.CreateEntry("model.manifest.json").Open());
            json.Write(manifest);
        }
        using var body = new MultipartFormDataContent();
        body.Add(new StringContent("response-test"), "name");
        body.Add(new StringContent("Padim"), "modelKind");
        body.Add(new ByteArrayContent(package.ToArray()), "file", "fixture.zip");
        using var response = await client.PostAsync("/api/anomalib/models", body, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var model = await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        Assert.True(model.TryGetProperty("ProjectId", out var project));
        Assert.True(model.TryGetProperty("RunId", out var run));
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync($"/api/anomalib/models/{project.GetString()}/{run.GetString()}", TestContext.Current.CancellationToken)).StatusCode);
    }

    /// <summary>未提交 TensorRT 字段的普通表单不应被 MVC 创建的默认对象误判。</summary>
    /// <param name="path">普通识别或绘制识别的实际路由。</param>
    [Theory]
    [InlineData("/api/yolo/Identity")]
    [InlineData("/api/yolo/IdentityDraw")]
    public async Task CpuInference_OmittedTensorRtDoesNotEnableTensorRt(string path)
    {
        await using var factory = new ApiRuntimeApplicationFactory();
        using var client = factory.CreateClient();
        factory.Services.GetRequiredService<OnnxRuntimeService>().Configure(new());
        using var bitmap = new SKBitmap(32, 32);
        bitmap.Erase(SKColors.Gray);
        using var png = bitmap.Encode(SKEncodedImageFormat.Png, 90);
        using var body = new MultipartFormDataContent();
        body.Add(new StringContent("-1"), "onnxIndex");
        body.Add(new StringContent("{}"), "paramJson");
        body.Add(new ByteArrayContent(png.ToArray()), "file", "fixture.png");
        using var response = await client.PostAsync(path, body, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        Assert.DoesNotContain("TensorRT", result.GetProperty("Message").GetString());
    }

    /// <summary>GPU 默认配置不应阻止无 CUDA 环境查询健康状态、文档或切换 CPU。</summary>
    [Fact]
    public async Task DefaultGpu_CanQuerySwitchToCpuAndRestorePersistedSettings()
    {
        await using var factory = new ApiRuntimeApplicationFactory();
        using var client = factory.CreateClient();
        var cancellation = TestContext.Current.CancellationToken;
        var before = await client.GetFromJsonAsync<JsonElement>("/api/runtime/hardware", cancellation);
        Assert.Equal("Cuda", before.GetProperty("Hardware").GetString());
        Assert.Equal(0, before.GetProperty("GpuId").GetInt32());
        var health = await client.GetFromJsonAsync<JsonElement>("/health", cancellation);
        Assert.Equal("Healthy", health.GetProperty("status").GetString());
        using var request = new StringContent("{\"Hardware\":\"Cpu\",\"GpuId\":0}", Encoding.UTF8, "application/json");
        using var changed = await client.PutAsync("/api/runtime/hardware", request, cancellation);
        Assert.Equal(HttpStatusCode.OK, changed.StatusCode);
        Assert.Equal(OnnxRuntimeHardware.Cpu, new OnnxRuntimeSettingsStore(factory.SettingsPath).Current.Hardware);
        var after = await client.GetFromJsonAsync<JsonElement>("/api/runtime/hardware", cancellation);
        Assert.Equal("Cpu", after.GetProperty("Hardware").GetString());
        var swagger = await client.GetFromJsonAsync<JsonElement>("/swagger/v1/swagger.json", cancellation);
        var paths = swagger.GetProperty("paths");
        Assert.True(paths.GetProperty("/api/runtime/hardware").TryGetProperty("put", out _));
        Assert.True(paths.TryGetProperty("/api/yolo/Identity", out _));
        Assert.True(paths.TryGetProperty("/api/yolo/QueryAll", out _));
        Assert.True(paths.TryGetProperty("/api/anomalib/models/{projectId}/{runId}/identify", out _));
    }

    /// <summary>未知类型、缺少必填字段及负 GPU 编号必须在变更配置前失败。</summary>
    /// <param name="body">非法请求 JSON。</param>
    [Theory]
    [InlineData("{}")]
    [InlineData("{\"Hardware\":\"Unknown\"}")]
    [InlineData("{\"Hardware\":987}")]
    [InlineData("{\"Hardware\":\"Cuda\",\"GpuId\":-1}")]
    public async Task InvalidRequests_PreservePreviousConfiguration(string body)
    {
        await using var factory = new ApiRuntimeApplicationFactory();
        using var client = factory.CreateClient();
        using var content = new StringContent(body, Encoding.UTF8, "application/json");
        using var response = await client.PutAsync("/api/runtime/hardware", content, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(OnnxRuntimeHardware.Cuda, factory.Services.GetRequiredService<OnnxRuntimeSettingsStore>().Current.Hardware);
        Assert.False(File.Exists(factory.SettingsPath));
    }

    /// <summary>无效 GPU 或缺少驱动/运行库返回 503，原 CPU 配置及缓存代次不变。</summary>
    [Fact]
    public async Task UnavailableGpu_PreservesCpuAndExistingSessions()
    {
        await using var factory = new ApiRuntimeApplicationFactory();
        using var client = factory.CreateClient();
        var service = factory.Services.GetRequiredService<OnnxRuntimeService>();
        service.Configure(new());
        var before = service.CaptureSnapshot();
        using var body = new StringContent("{\"Hardware\":\"Cuda\",\"GpuId\":2147483647}", Encoding.UTF8, "application/json");
        using var response = await client.PutAsync("/api/runtime/hardware", body, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal(before.Settings, service.Current);
        Assert.Equal(before.Generation, service.CaptureSnapshot().Generation);
        Assert.Equal(OnnxRuntimeHardware.Cpu, new OnnxRuntimeSettingsStore(factory.SettingsPath).Current.Hardware);
    }

    /// <summary>GPU 探测使用的微型 ONNX 必须可被真实 CPU 运行库读取，排除探测模型损坏。</summary>
    [Fact]
    public void HardwareProbe_IsValidOnnx()
    {
        var model = (byte[])typeof(OnnxRuntimeService).GetField("ProbeModel", BindingFlags.NonPublic | BindingFlags.Static)!.GetValue(null)!;
        using var options = OnnxSessionOptions.CreateCpu();
        using var session = new InferenceSession(model, options);
        Assert.Equal("x", Assert.Single(session.InputNames));
        Assert.Equal("y", Assert.Single(session.OutputNames));
    }

    /// <summary>存储和硬件工厂共享不可变快照；API 默认值不会影响 Tasks 的 CPU 默认值。</summary>
    [Fact]
    public void Capture_PreservesGpuSnapshotAfterSwitchToCpu()
    {
        var path = Path.Combine(Path.GetTempPath(), "snet-api-capture-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            var store = new OnnxRuntimeSettingsStore(path, new() { Hardware = OnnxRuntimeHardware.Cuda, GpuId = 3 });
            using var cache = new InferenceSessionCache();
            var service = new OnnxRuntimeService(store, cache);
            var before = service.CaptureSnapshot();
            service.Configure(new());
            Assert.Equal("cuda:3", before.CacheKey);
            Assert.Equal("cpu", service.CaptureSnapshot().CacheKey);
            Assert.True(service.CaptureSnapshot().Generation > before.Generation);
            using var cpu = service.Create();
            Assert.True(cpu.EnableCpuMemArena);
        }
        finally { File.Delete(path); }
    }
}
