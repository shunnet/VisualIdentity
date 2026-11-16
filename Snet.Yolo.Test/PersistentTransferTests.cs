using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using SkiaSharp;
using Snet.Yolo.Server;
using Snet.Yolo.Server.models;
using Snet.Yolo.Tasks.Core.Models;
using Snet.Yolo.Tasks.Core.Serialization.Export;
using Snet.Yolo.Tasks.Core.Workspace;
using Snet.Yolo.Tasks.Services;
using Xunit;

namespace Snet.Yolo.Test;

/// <summary>真实分块、进程级重绑定、业务导入、取消、认证与 Range 下载回归；不执行模型推理。</summary>
[Collection("Database")]
public sealed class PersistentTransferTests(ValidationMediaAccessTests.TasksFactory factory) : IClassFixture<ValidationMediaAccessTests.TasksFactory>
{
    /// <summary>已确认偏移可从磁盘恢复；重复分块被拒绝，重复完成不会重复导入图片。</summary>
    [Theory]
    [InlineData(UploadKind.ProjectImages)]
    [InlineData(UploadKind.ProjectClassImages)]
    [InlineData(UploadKind.AnomalibProjectImages)]
    [InlineData(UploadKind.ProjectYoloArchive)]
    public async Task Upload_RestoresOffsetsAndImportsExactlyOnce(UploadKind kind)
    {
        var token = TestContext.Current.CancellationToken;
        var workspace = Workspace();
        var project = Project(kind == UploadKind.AnomalibProjectImages ? ProjectKind.Anomalib : ProjectKind.Yolo);
        await workspace.SaveProjectAsync(project, token);
        var transfers = factory.Services.GetRequiredService<PersistentUploadService>();
        var bytes = kind == UploadKind.ProjectYoloArchive ? ZipHelper.Pack([new("classes.txt", "0 good"u8.ToArray()), new("images/sample.png", Png()), new("labels/sample.txt", [])]) : Png();
        var principal = Principal();
        var job = await transfers.CreateAsync(principal, new(kind, "persistent:" + project.Id, project.Id, ClassName: "good"), new(), [new(kind == UploadKind.ProjectYoloArchive ? "dataset.zip" : "sample.png", bytes.Length)]);
        try
        {
            var half = bytes.Length / 2;
            await transfers.AppendAsync(job, 0, 0, new MemoryStream(bytes[..half]), token);
            Assert.Equal(half, job.Offsets[0]);
            Assert.Null(transfers.Find("another-user", job.Id));
            var restored = new PersistentUploadService(factory.Services.GetRequiredService<IServiceScopeFactory>(), NullLogger<PersistentUploadService>.Instance);
            var resumed = Assert.IsType<PersistentUpload>(restored.Find(factory.Username, job.Id));
            Assert.Equal(half, resumed.Offsets[0]);
            await Assert.ThrowsAsync<InvalidOperationException>(() => restored.AppendAsync(resumed, 0, 0, new MemoryStream(bytes[..half]), token));
            await Assert.ThrowsAsync<InvalidOperationException>(() => restored.CompleteAsync(resumed, principal));
            await restored.AppendAsync(resumed, 0, half, new MemoryStream(bytes[half..]), token);
            await restored.CompleteAsync(resumed, principal);
            await restored.CompleteAsync(resumed, principal);
            await WaitForAsync(() => resumed.State is not ("processing" or "uploading"));
            Assert.Equal("completed", resumed.State);
            var saved = Assert.IsType<WorkspaceProject>(await workspace.GetProjectAsync(project.Id, token));
            Assert.Single(saved.Tasks);
            Assert.Empty(Directory.GetFiles(Path.Combine(AppContext.BaseDirectory, "transfers", "uploads", job.Id), "*.part"));
            restored.Clear(factory.Username, job.Intent.ScopeKey);
        }
        finally { await transfers.CancelAsync(job); transfers.Clear(factory.Username, job.Intent.ScopeKey); await workspace.DeleteProjectAsync(project.Id); }
    }

    /// <summary>超长分块不能推进偏移，取消后不可追加且不会修改工程。</summary>
    [Fact]
    public async Task Upload_RejectsOversizeAndCancellationCleansParts()
    {
        var workspace = Workspace(); var project = Project(ProjectKind.Yolo);
        await workspace.SaveProjectAsync(project, TestContext.Current.CancellationToken);
        var transfers = factory.Services.GetRequiredService<PersistentUploadService>();
        var job = await transfers.CreateAsync(Principal(), new(UploadKind.ProjectImages, "cancel:" + project.Id, project.Id), new(), [new("sample.png", 10)]);
        try
        {
            await Assert.ThrowsAsync<InvalidDataException>(() => transfers.AppendAsync(job, 0, 0, new MemoryStream(new byte[11]), TestContext.Current.CancellationToken));
            Assert.Equal(0, job.Offsets[0]);
            await transfers.AppendAsync(job, 0, 0, new MemoryStream(new byte[5]), TestContext.Current.CancellationToken);
            await transfers.CancelAsync(job);
            Assert.Equal("cancelled", job.State);
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => transfers.AppendAsync(job, 0, 5, new MemoryStream(new byte[5]), TestContext.Current.CancellationToken));
            Assert.Empty(Directory.GetFiles(Path.Combine(AppContext.BaseDirectory, "transfers", "uploads", job.Id), "*.part"));
            Assert.Empty((await workspace.GetProjectAsync(project.Id))!.Tasks);
        }
        finally { await transfers.CancelAsync(job); transfers.Clear(factory.Username, job.Intent.ScopeKey); await workspace.DeleteProjectAsync(project.Id); }
    }

    /// <summary>顶部取消也必须打断尚未读完的慢分块，不能等待请求拿着的互斥锁。</summary>
    [Fact]
    public async Task Upload_CancelInterruptsBlockedChunkWithoutWaitingForBody()
    {
        var workspace = Workspace(); var project = Project(ProjectKind.Yolo);
        await workspace.SaveProjectAsync(project, TestContext.Current.CancellationToken);
        var transfers = factory.Services.GetRequiredService<PersistentUploadService>();
        var job = await transfers.CreateAsync(Principal(), new(UploadKind.ProjectImages, "blocked:" + project.Id, project.Id), new(), [new("sample.png", 1024)]);
        var pipe = new System.IO.Pipelines.Pipe();
        try
        {
            var append = transfers.AppendAsync(job, 0, 0, pipe.Reader.AsStream(), TestContext.Current.CancellationToken);
            await transfers.CancelAsync(job).WaitAsync(TimeSpan.FromSeconds(3), TestContext.Current.CancellationToken);
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => append);
            Assert.Equal("cancelled", job.State);
            Assert.Empty(Directory.GetFiles(Path.Combine(AppContext.BaseDirectory, "transfers", "uploads", job.Id), "*.part"));
        }
        finally { await pipe.Writer.CompleteAsync(); await pipe.Reader.CompleteAsync(); transfers.Clear(factory.Username, job.Intent.ScopeKey); await workspace.DeleteProjectAsync(project.Id); }
    }

    /// <summary>原来属于页面的品牌和模型包，以及常驻 ONNX/联合图片，都由后台作用域完成。</summary>
    [Theory]
    [InlineData(UploadKind.BrandLogo)]
    [InlineData(UploadKind.AnomalibModelPackage)]
    [InlineData(UploadKind.ValidationModel)]
    [InlineData(UploadKind.JointValidationImages)]
    public async Task StandaloneUpload_CompletesWithoutPage(UploadKind kind)
    {
        var transfers = factory.Services.GetRequiredService<PersistentUploadService>();
        var models = factory.Services.GetRequiredService<ManageOperate>();
        var registry = factory.Services.GetRequiredService<Snet.Yolo.Server.anomalib.AnomalibModelRegistry>();
        var before = await models.ListAvailableByOwnerAsync(factory.Username, TestContext.Current.CancellationToken);
        var source = ApiModelFixtures.AnomalibOnnx;
        var digest = Convert.ToHexStringLower(SHA256.HashData(source));
        var manifest = $$$"""
        {"schemaVersion":"1.0","family":"anomalib","anomalibVersion":"2.6.2","algorithm":"padim","modelSha256":"{{{digest}}}",
         "input":{"name":"image","elementType":"float32","layout":"nchw","colorSpace":"rgb","width":32,"height":32,"resizeMode":"stretch","valueRange":"zeroToOne","normalizationEmbedded":true},
         "outputs":{"predictionScore":"pred_score","predictionLabel":"pred_label","anomalyMap":"anomaly_map","predictionMask":"pred_mask"},"postProcessing":{"threshold":0.5,"thresholdSource":"manual"}}
        """;
        var bytes = kind == UploadKind.AnomalibModelPackage ? ZipHelper.Pack([new("model.onnx", source), new("model.manifest.json", System.Text.Encoding.UTF8.GetBytes(manifest))])
            : kind == UploadKind.ValidationModel ? source : Png();
        var name = kind == UploadKind.AnomalibModelPackage ? "model.zip" : kind == UploadKind.ValidationModel ? "model.onnx" : "image.png";
        var principal = kind == UploadKind.BrandLogo ? new ClaimsPrincipal(new ClaimsIdentity([new(ClaimTypes.Name, factory.Username), new(ClaimTypes.Role, "Admin")], "transfer-test")) : Principal();
        var scope = "standalone:" + Guid.NewGuid().ToString("N");
        var job = await transfers.CreateAsync(principal, new(kind, scope), new() { ModelName = "独立模型上传" }, [new(name, bytes.Length)]);
        try
        {
            await transfers.AppendAsync(job, 0, 0, new MemoryStream(bytes), TestContext.Current.CancellationToken);
            await transfers.CompleteAsync(job, principal);
            await WaitForAsync(() => job.State is not ("uploading" or "processing"));
            Assert.True(job.State == "completed", job.Error);
            if (kind == UploadKind.BrandLogo) { Assert.StartsWith("data:image/png;base64,", factory.Services.GetRequiredService<BrandLogoDraftStore>().Read(factory.Username)); }
            if (kind == UploadKind.AnomalibModelPackage) { Assert.Single(await registry.ListAsync(factory.Username, TestContext.Current.CancellationToken)); }
            if (kind == UploadKind.ValidationModel) { Assert.Equal(before.Count + 1, (await models.ListAvailableByOwnerAsync(factory.Username, TestContext.Current.CancellationToken)).Count); }
            if (kind == UploadKind.JointValidationImages) { Assert.Single(factory.Services.GetRequiredService<JointValidationState>().Get(factory.Username).Images); }
        }
        finally
        {
            await transfers.CancelAsync(job); transfers.Clear(factory.Username, scope);
            factory.Services.GetRequiredService<BrandLogoDraftStore>().Clear(factory.Username);
            foreach (var model in await registry.ListAsync(factory.Username, TestContext.Current.CancellationToken)) { await registry.DeleteAsync(factory.Username, model.ProjectId, model.RunId, TestContext.Current.CancellationToken); }
            foreach (var model in (await models.ListAvailableByOwnerAsync(factory.Username, TestContext.Current.CancellationToken)).Where(model => !before.Any(old => old.index == model.index)))
            { await models.DeleteAsync(factory.Username, model.index, true, TestContext.Current.CancellationToken); }
        }
    }

    /// <summary>不能为其他用户工程、模型或普通用户品牌设置建立上传任务。</summary>
    [Theory]
    [InlineData(UploadKind.ProjectImages)]
    [InlineData(UploadKind.AnomalibProjectImages)]
    [InlineData(UploadKind.ValidationImages)]
    [InlineData(UploadKind.AnomalibValidationImages)]
    [InlineData(UploadKind.BrandLogo)]
    public async Task Upload_RejectsUnownedTargets(UploadKind kind)
    {
        var transfers = factory.Services.GetRequiredService<PersistentUploadService>();
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => transfers.CreateAsync(Principal(), new(kind, "invalid", "missing", ModelIndex: -1, RunId: "missing"), new(), [new("sample.png", 20)]));
    }

    /// <summary>HTTP 分块必须有真实 Cookie 和防伪令牌，未知任务不泄露数据。</summary>
    [Fact]
    public async Task Upload_HttpRequiresAntiforgeryAndOwner()
    {
        using var client = await SignInAsync();
        using var invalid = await client.PutAsync("/api/transfers/uploads/missing/0?offset=0", new ByteArrayContent([1]), TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        var csrf = await client.GetFromJsonAsync<JsonElement>("/api/transfers/token", TestContext.Current.CancellationToken);
        using var request = new HttpRequestMessage(HttpMethod.Put, "/api/transfers/uploads/missing/0?offset=0") { Content = new ByteArrayContent([1]) };
        request.Headers.Add("RequestVerificationToken", csrf.GetProperty("token").GetString());
        using var missing = await client.SendAsync(request, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
    }

    /// <summary>刷新前后的同一下载请求返回同一产物；Range 与 If-Range 保持稳定且隔离所有者。</summary>
    [Fact]
    public async Task Download_PersistsArtifactAndSupportsRangeAcrossRequests()
    {
        var workspace = Workspace(); var project = Project(ProjectKind.Yolo);
        await workspace.SaveProjectAsync(project, TestContext.Current.CancellationToken);
        try
        {
            using var client = await SignInAsync();
            var url = $"/api/projects/{project.Id}/export?format={Uri.EscapeDataString(ExportFormats.Yolo)}&persistent=1&transfer={Guid.NewGuid():N}";
            var job = await client.GetFromJsonAsync<JsonElement>(url, TestContext.Current.CancellationToken);
            var id = job.GetProperty("id").GetString();
            var downloads = factory.Services.GetRequiredService<PersistentDownloadService>();
            var durable = Assert.IsType<PersistentDownload>(downloads.Find(factory.Username, id!));
            await durable.Completion.WaitAsync(TestContext.Current.CancellationToken);
            Assert.Equal("ready", durable.State);
            Assert.Null(downloads.Find("another-user", id!));
            var restored = new PersistentDownloadService(factory.Services.GetRequiredService<IServiceScopeFactory>(), NullLogger<PersistentDownloadService>.Instance);
            Assert.Equal("ready", restored.Find(factory.Username, id!)!.State);
            var same = await client.GetFromJsonAsync<JsonElement>(url, TestContext.Current.CancellationToken);
            Assert.Equal(id, same.GetProperty("id").GetString());
            var path = $"/api/transfers/downloads/{id}/file";
            using var full = await client.GetAsync(path, TestContext.Current.CancellationToken);
            var bytes = await full.Content.ReadAsByteArrayAsync(TestContext.Current.CancellationToken);
            Assert.NotEmpty(bytes); Assert.Equal("bytes", Assert.Single(full.Headers.AcceptRanges));
            using var request = new HttpRequestMessage(HttpMethod.Get, path);
            request.Headers.Range = new(3, 9); request.Headers.IfRange = new(full.Headers.ETag!);
            using var partial = await client.SendAsync(request, TestContext.Current.CancellationToken);
            Assert.Equal(HttpStatusCode.PartialContent, partial.StatusCode);
            Assert.Equal(bytes[3..10], await partial.Content.ReadAsByteArrayAsync(TestContext.Current.CancellationToken));
            using var beyond = new HttpRequestMessage(HttpMethod.Get, path);
            beyond.Headers.Range = new(bytes.Length + 1, null);
            using var unsatisfiable = await client.SendAsync(beyond, TestContext.Current.CancellationToken);
            Assert.Equal(HttpStatusCode.RequestedRangeNotSatisfiable, unsatisfiable.StatusCode);
            Assert.NotNull(await workspace.GetProjectAsync(project.Id));
        }
        finally { await workspace.DeleteProjectAsync(project.Id); }
    }

    /// <summary>构造可信测试身份，不能由浏览器请求体指定。</summary>
    private ClaimsPrincipal Principal() => new(new ClaimsIdentity([new(ClaimTypes.Name, factory.Username), new(ClaimTypes.Role, "User")], "transfer-test"));
    /// <summary>准备真实工作区。</summary>
    private WorkspaceService Workspace() => new(factory.Services.GetRequiredService<ProjectOperate>(), factory.Services.GetRequiredService<ProjectTaskOperate>(), factory.Services.GetRequiredService<TrainingService>(), new CurrentUserContext(new UploadTestAuthentication(factory.Username)), NullLogger<WorkspaceService>.Instance);
    /// <summary>最小真实工程。</summary>
    private static WorkspaceProject Project(ProjectKind kind) => new() { Kind = kind, Name = "持久传输回归", LabelConfigXml = "<View><Image name='image' value='$image'/><RectangleLabels name='box' toName='image'><Label value='good'/></RectangleLabels></View>" };
    /// <summary>生成可解码图片。</summary>
    private static byte[] Png() { using var bitmap = new SKBitmap(32, 24); bitmap.Erase(SKColors.Coral); using var image = bitmap.Encode(SKEncodedImageFormat.Png, 100); return image.ToArray(); }
    /// <summary>等待后台导入的可观察终态。</summary>
    private static async Task WaitForAsync(Func<bool> condition)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken); timeout.CancelAfter(TimeSpan.FromSeconds(15));
        while (!condition()) { await Task.Delay(20, timeout.Token); }
    }
    /// <summary>使用真实登录和防伪，不绕过 HTTP 身份验证。</summary>
    private async Task<HttpClient> SignInAsync()
    {
        var client = factory.CreateClient(new() { AllowAutoRedirect = false });
        var html = await client.GetStringAsync("/login", TestContext.Current.CancellationToken);
        var match = Regex.Match(html, "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\""); Assert.True(match.Success);
        using var response = await client.PostAsync("/auth/login", new FormUrlEncodedContent(new Dictionary<string, string> { ["username"] = factory.Username, ["password"] = factory.Password, ["__RequestVerificationToken"] = WebUtility.HtmlDecode(match.Groups[1].Value) }), TestContext.Current.CancellationToken);
        Assert.Equal("/projects", response.Headers.Location?.ToString()); return client;
    }
}
