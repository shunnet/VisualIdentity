using System.IO.Pipelines;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using SkiaSharp;
using Snet.Yolo.Tasks.Components.Pages;
using Snet.Yolo.Tasks.Components.Shell;
using Snet.Yolo.Tasks.Core.Localization;
using Snet.Yolo.Tasks.Services;
using Xunit;

namespace Snet.Yolo.Test;

/// <summary>通过真实上传中心和可控浏览器流验证跨页上传、进度、取消及模型隔离，不执行模型推理。</summary>
public sealed class ValidationUploadTests
{
    /// <summary>品牌图片也支持中途取消；旧预览和后续上传能力保留。</summary>
    [Fact]
    public async Task LogoUpload_CancelPreservesPreviousPreviewAndAllowsRetry()
    {
        var page = new Settings();
        var members = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
        typeof(Settings).GetField("_admin", members)!.SetValue(page, true);
        typeof(Settings).GetField("_logo", members)!.SetValue(page, "previous-logo");
        for (var attempt = 0; attempt < 2; attempt++)
        {
            var pipe = new Pipe();
            try
            {
                var upload = (Task)typeof(Settings).GetMethod("UploadLogoAsync", members)!.Invoke(page,
                    [new InputFileChangeEventArgs([new JointUploadBrowserFile(pipe.Reader.AsStream(), 1024)])])!;
                Assert.False(upload.IsCompleted);
                Assert.True((bool)typeof(Settings).GetField("_uploading", members)!.GetValue(page)!);
                typeof(Settings).GetMethod("CancelLogoUpload", members)!.Invoke(page, null);
                await upload.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
                Assert.False((bool)typeof(Settings).GetField("_uploading", members)!.GetValue(page)!);
                Assert.Equal("previous-logo", typeof(Settings).GetField("_logo", members)!.GetValue(page));
            }
            finally { await pipe.Writer.CompleteAsync(); }
        }
    }

    /// <summary>页面释放和其他页面重新声明输入意图不影响已开始批次；回到原模型可以恢复文件。</summary>
    [Theory]
    [InlineData(UploadKind.ValidationImages)]
    [InlineData(UploadKind.AnomalibValidationImages)]
    [InlineData(UploadKind.JointValidationImages)]
    public async Task Navigation_KeepsOriginalUploadTargetAndCompletedFiles(UploadKind kind)
    {
        var owner = "upload-" + Guid.NewGuid().ToString("N");
        using var lifetime = new ValidationFileLifetime();
        var previews = new ImagePreviewStore(new(), lifetime, NullLogger<ImagePreviewStore>.Instance);
        var user = new CurrentUserContext(new UploadTestAuthentication(owner));
        var validation = new ValidationService(null!, null!, user, null!, null!, null!, lifetime, previews, NullLogger<ValidationService>.Instance);
        var yolo = new ValidationState();
        var anomalib = new AnomalibValidationState();
        var joint = new JointValidationState();
        var language = new LanguageManager();
        using var uploads = new UploadCenter(null!, validation, yolo, new(), user, language, null!, previews, NullLogger<UploadCenter>.Instance, anomalib, joint);
        anomalib.Save(owner, new("other/run", null, [], [], .8f, 9));
        var pipe = new Pipe();
        var bytes = ImageBytes();
        var scope = "test:" + kind;
        uploads.Arm(new(kind, scope, "project", ModelIndex: 7, RunId: "run"));
        await uploads.OnImagesSelectedAsync(new([new JointUploadBrowserFile(pipe.Reader.AsStream(), bytes.Length)]));
        try
        {
            await WaitForAsync(() => uploads.GetJob(scope)?.IsRunning == true);
            if (kind == UploadKind.AnomalibValidationImages)
            {
                var page = new AnomalibValidation();
                Inject(page, "Language", language); Inject(page, "Uploads", uploads);
                await page.DisposeAsync();
            }
            else if (kind == UploadKind.JointValidationImages)
            {
                var page = new JointValidation();
                Inject(page, "Language", language); Inject(page, "Uploads", uploads);
                await page.DisposeAsync();
            }
            uploads.Arm(new(UploadKind.JointValidationImages, "another-page"));
            Assert.True(uploads.GetJob(scope)!.IsRunning);
            await pipe.Writer.WriteAsync(bytes, TestContext.Current.CancellationToken);
            await pipe.Writer.CompleteAsync();
            await WaitForAsync(() => uploads.GetJob(scope)?.Phase == UploadPhase.Completed);
            Assert.Single(kind switch
            {
                UploadKind.ValidationImages => yolo.GetModel(owner, 7).Images.Select(image => image.Url),
                UploadKind.AnomalibValidationImages => anomalib.GetModel(owner, "project/run").Files.Select(file => file.Url),
                _ => joint.Get(owner).Images.Select(image => image.Url),
            });
            Assert.Empty(yolo.GetModel(owner, 8).Images);
            Assert.Empty(anomalib.GetModel(owner, "other/run").Files);
            Assert.Equal("other/run", anomalib.Get(owner).SelectedModelKey);
            Assert.Null(uploads.GetJob("another-page"));
        }
        finally
        {
            uploads.Cancel(scope);
            await pipe.Writer.CompleteAsync();
            await WaitForAsync(() => !uploads.Busy);
        }
    }

    /// <summary>实际共享进度组件显示大小、百分比、文件数及取消；取消清理半文件，保留已完成文件，允许重试。</summary>
    [Theory]
    [InlineData(UploadKind.ValidationImages, "zh-CN")]
    [InlineData(UploadKind.AnomalibValidationImages, "zh-CN")]
    [InlineData(UploadKind.JointValidationImages, "en-US")]
    public async Task Cancel_ShowsByteProgressAndPreservesCompletedFiles(UploadKind kind, string culture)
    {
        var owner = "upload-cancel-" + Guid.NewGuid().ToString("N");
        using var lifetime = new ValidationFileLifetime();
        var previews = new ImagePreviewStore(new(), lifetime, NullLogger<ImagePreviewStore>.Instance);
        var user = new CurrentUserContext(new UploadTestAuthentication(owner));
        var validation = new ValidationService(null!, null!, user, null!, null!, null!, lifetime, previews, NullLogger<ValidationService>.Instance);
        var yolo = new ValidationState(); var anomalib = new AnomalibValidationState(); var joint = new JointValidationState();
        var language = new LanguageManager(); language.SetLanguage(culture);
        using var uploads = new UploadCenter(null!, validation, yolo, new(), user, language, null!, previews, NullLogger<UploadCenter>.Instance, anomalib, joint);
        var scope = "cancel:" + kind;
        var intent = new UploadIntent(kind, scope, "project", ModelIndex: 7, RunId: "run");
        var bytes = ImageBytes();
        var pipe = new Pipe();
        uploads.Arm(intent);
        await uploads.OnImagesSelectedAsync(new([
            new JointUploadBrowserFile(new MemoryStream(bytes), bytes.Length),
            new JointUploadBrowserFile(pipe.Reader.AsStream(), 2 * 1024 * 1024)]));
        try
        {
            await WaitForAsync(() => uploads.GetJob(scope)?.Completed == 1);
            await pipe.Writer.WriteAsync(new byte[1024 * 1024], TestContext.Current.CancellationToken);
            await WaitForAsync(() => uploads.GetJob(scope)?.Status.Contains("1 MB / 2 MB") == true);
            var services = new ServiceCollection().AddLogging().AddSingleton(uploads).AddSingleton(language).BuildServiceProvider();
            await using (services)
            await using (var renderer = new HtmlRenderer(services, services.GetRequiredService<ILoggerFactory>()))
            {
                var html = await renderer.Dispatcher.InvokeAsync(async () =>
                {
                    var rendered = await renderer.RenderComponentAsync<UploadStatus>(ParameterView.FromDictionary(new Dictionary<string, object?> { ["ScopeKey"] = scope }));
                    return rendered.ToHtmlString();
                });
                Assert.Contains("1 MB / 2 MB", html);
                Assert.Contains("75%", html);
                Assert.Contains("1 / 2", html);
                Assert.Contains(language.Translate("Cancel"), System.Net.WebUtility.HtmlDecode(html));
                Assert.Contains("bi-stop-circle", html);
            }
            uploads.Cancel(scope);
            await WaitForAsync(() => uploads.GetJob(scope)?.Phase == UploadPhase.Cancelled);
            Assert.Equal(1, uploads.GetJob(scope)!.Completed);
            var folder = kind switch { UploadKind.ValidationImages => "validation", UploadKind.AnomalibValidationImages => "anomalib-validation", _ => "joint-validation" };
            var directory = Path.Combine(AppContext.BaseDirectory, "wwwroot", "data", "uploads", owner, folder);
            Assert.Empty(Directory.GetFiles(directory, "*.upload"));
            Assert.Single(Directory.GetFiles(directory, "*.png"));
            uploads.Arm(intent);
            await uploads.OnImagesSelectedAsync(new([new JointUploadBrowserFile(new MemoryStream(bytes), bytes.Length)]));
            await WaitForAsync(() => uploads.GetJob(scope)?.Phase == UploadPhase.Completed);
            Assert.Equal(2, Directory.GetFiles(directory, "*.png").Length);
        }
        finally
        {
            uploads.Cancel(scope);
            await pipe.Writer.CompleteAsync();
            await WaitForAsync(() => !uploads.Busy);
        }
    }

    /// <summary>生成真实小图，避免通过假文件绕过格式校验。</summary>
    private static byte[] ImageBytes()
    {
        using var bitmap = new SKBitmap(32, 32);
        bitmap.Erase(SKColors.CornflowerBlue);
        using var image = bitmap.Encode(SKEncodedImageFormat.Png, 100);
        return image.ToArray();
    }

    /// <summary>等待可观察状态，超时直接失败，避免依赖机器速度。</summary>
    private static async Task WaitForAsync(Func<bool> condition)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(10));
        while (!condition()) { await Task.Delay(20, timeout.Token); }
    }

    /// <summary>给未挂载的页面注入释放所需服务，执行真实 DisposeAsync。</summary>
    private static void Inject(object page, string property, object value)
        => page.GetType().GetProperty(property, System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.SetValue(page, value);
}
