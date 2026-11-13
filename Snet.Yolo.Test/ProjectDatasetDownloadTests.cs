using System.IO.Compression;
using System.Net;
using System.Text.RegularExpressions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Snet.Yolo.Server;
using Snet.Yolo.Tasks.Core.Models;
using Snet.Yolo.Tasks.Core.Serialization.Export;
using Snet.Yolo.Tasks.Core.Workspace;
using Snet.Yolo.Tasks.Services;
using Xunit;

namespace Snet.Yolo.Test;

/// <summary>通过真实 Tasks HTTP 宿主验证数据集下载、身份隔离和失败诊断，不绕过 Cookie 与防伪登录。</summary>
[Collection("Database")]
public sealed class ProjectDatasetDownloadTests(ValidationMediaAccessTests.TasksFactory factory) : IClassFixture<ValidationMediaAccessTests.TasksFactory>
{
    /// <summary>标注和含原图导出均从持久化工程读取，生成可解压且编号一致的 ZIP。</summary>
    [Theory]
    [InlineData(ExportFormats.Yolo)]
    [InlineData(ExportFormats.YoloWithImages)]
    public async Task Owner_DownloadsCompleteDatasetOverHttp(string format)
    {
        var token = TestContext.Current.CancellationToken;
        var workspace = Workspace(factory.Username);
        var project = CreateProject();
        var root = (await workspace.GetProjectUploadLocationAsync(project.Id)).Directory;
        Directory.CreateDirectory(root);
        var bytes = new byte[] { 1, 2, 3, 4, 5 };
        try
        {
            await File.WriteAllBytesAsync(Path.Combine(root, "one.png"), bytes, token);
            await File.WriteAllBytesAsync(Path.Combine(root, "two.png"), bytes.Reverse().ToArray(), token);
            await workspace.SaveProjectAsync(project, token);
            using var client = await SignInAsync();
            using var request = new HttpRequestMessage(HttpMethod.Get, Url(project.Id, format));
            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal("application/zip", response.Content.Headers.ContentType?.MediaType);
            Assert.Contains("attachment", response.Content.Headers.ContentDisposition?.ToString());
            Assert.True(response.Headers.CacheControl?.NoStore);
            Assert.Equal("no", Assert.Single(response.Headers.GetValues("X-Accel-Buffering")));
            await using var body = await response.Content.ReadAsStreamAsync(token);
            using var archive = new ZipArchive(body, ZipArchiveMode.Read);
            using (var reader = new StreamReader(Assert.IsType<ZipArchiveEntry>(archive.GetEntry("labels/1.txt")).Open()))
            {
                Assert.Equal("0 0.25 0.4 0.3 0.4", (await reader.ReadToEndAsync(token)).Trim());
            }
            if (format == ExportFormats.YoloWithImages)
            {
                Assert.Equal(5, archive.Entries.Count);
                Assert.Equal(0, archive.GetEntry("labels/2.txt")!.Length);
                for (var index = 1; index <= 2; index++)
                {
                    await using var image = await archive.GetEntry($"images/{index}.png")!.OpenAsync(token);
                    using var copy = new MemoryStream();
                    await image.CopyToAsync(copy, token);
                    Assert.Equal(index == 1 ? bytes : bytes.Reverse().ToArray(), copy.ToArray());
                }
            }
            else { Assert.DoesNotContain(archive.Entries, entry => entry.FullName.StartsWith("images/", StringComparison.Ordinal)); }
        }
        finally { await workspace.DeleteProjectAsync(project.Id); }
    }

    /// <summary>下载接口不能读取别人的工程，匿名请求仍要求登录，未知格式不会落入任意导出器。</summary>
    [Fact]
    public async Task Download_RequiresOwnershipAndKnownFormat()
    {
        var workspace = Workspace("export-other-" + Guid.NewGuid().ToString("N"));
        var project = CreateProject();
        await workspace.SaveProjectAsync(project, TestContext.Current.CancellationToken);
        try
        {
            using var anonymous = factory.CreateClient(new() { AllowAutoRedirect = false });
            using var challenge = await anonymous.GetAsync(Url(project.Id, ExportFormats.Yolo), TestContext.Current.CancellationToken);
            Assert.Equal(HttpStatusCode.Redirect, challenge.StatusCode);
            using var client = await SignInAsync();
            using var inaccessible = await client.GetAsync(Url(project.Id, ExportFormats.Yolo), TestContext.Current.CancellationToken);
            Assert.Equal(HttpStatusCode.NotFound, inaccessible.StatusCode);
            using var invalid = await client.GetAsync(Url(project.Id, "invalid-format"), TestContext.Current.CancellationToken);
            Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        }
        finally { await workspace.DeleteProjectAsync(project.Id); }
    }

    /// <summary>原图缺失或引用包含编码后的目录跳转时，在 ZIP 开始前拒绝，不能静默少导图片。</summary>
    [Theory]
    [InlineData("missing.png", "missing.png")]
    [InlineData("%2e%2e%5csecret.png", "无效文件名")]
    public async Task Download_RejectsMissingOrUnsafeImagesBeforeWritingZip(string image, string reason)
    {
        var workspace = Workspace(factory.Username);
        var project = CreateProject();
        project.Tasks[0].Data!["image"] = image;
        await workspace.SaveProjectAsync(project, TestContext.Current.CancellationToken);
        try
        {
            using var client = await SignInAsync();
            using var response = await client.GetAsync(Url(project.Id, ExportFormats.YoloWithImages), TestContext.Current.CancellationToken);
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            var problem = System.Text.Json.Nodes.JsonNode.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
            Assert.Contains(reason, problem!["detail"]!.GetValue<string>());
            Assert.NotEqual("application/zip", response.Content.Headers.ContentType?.MediaType);
        }
        finally { await workspace.DeleteProjectAsync(project.Id); }
    }

    /// <summary>使用宿主的真实项目存储；可信身份替身仅用于准备当前测试的独立工程。</summary>
    private WorkspaceService Workspace(string owner) => new(
        factory.Services.GetRequiredService<ProjectOperate>(), factory.Services.GetRequiredService<ProjectTaskOperate>(),
        factory.Services.GetRequiredService<TrainingService>(), new CurrentUserContext(new UploadTestAuthentication(owner)), NullLogger<WorkspaceService>.Instance);

    /// <summary>构造一张已标注图片与一张空标注图片，验证导出不会漏掉正常样本。</summary>
    private static WorkspaceProject CreateProject() => new()
    {
        Name = "流式数据集回归",
        LabelConfigXml = "<View><Image name='image' value='$image'/><RectangleLabels name='box' toName='image'><Label value='缺陷'/></RectangleLabels></View>",
        Tasks = [new() { Id = 1, Data = new() { ["image"] = "one.png" }, Annotations = [new() { Result = [new() {
            Type = RegionType.RectangleLabels, FromName = "box", ToName = "image", OriginalWidth = 100, OriginalHeight = 100,
            Value = new() { ["x"] = 10, ["y"] = 20, ["width"] = 30, ["height"] = 40, ["rectanglelabels"] = new System.Text.Json.Nodes.JsonArray("缺陷") } }] }] },
            new() { Id = 2, Data = new() { ["image"] = "two.png" } }],
    };

    /// <summary>通过实际登录表单获取 Cookie，避免给下载接口替换匿名认证。</summary>
    private async Task<HttpClient> SignInAsync()
    {
        var client = factory.CreateClient(new() { AllowAutoRedirect = false });
        try
        {
            var html = await client.GetStringAsync("/login", TestContext.Current.CancellationToken);
            var token = Regex.Match(html, "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"");
            Assert.True(token.Success);
            using var response = await client.PostAsync("/auth/login", new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["username"] = factory.Username, ["password"] = factory.Password,
                ["__RequestVerificationToken"] = WebUtility.HtmlDecode(token.Groups[1].Value),
            }), TestContext.Current.CancellationToken);
            Assert.Equal("/projects", response.Headers.Location?.ToString());
            return client;
        }
        catch { client.Dispose(); throw; }
    }

    /// <summary>与页面相同的普通 HTTP 下载地址，参数使用 URI 编码。</summary>
    private static string Url(string projectId, string format) => $"/api/projects/{Uri.EscapeDataString(projectId)}/export?format={Uri.EscapeDataString(format)}";
}
