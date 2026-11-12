using Snet.Yolo.Server.anomalib;
using Snet.Yolo.Tasks.Components.Pages;
using Snet.Yolo.Tasks.Core.Localization;
using Snet.Yolo.Tasks.Services;
using System.Reflection;
using System.IO.Pipelines;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.AspNetCore.Components.RenderTree;
using Xunit;

namespace Snet.Yolo.Test;

/// <summary>执行联合验证页实际切换与保存方法，不依赖原生模型或运行中的浏览器。</summary>
public sealed class JointValidationInteractionTests
{
    /// <summary>真正开始上传并等待文件流时，Razor 应展示上传进度，不能误报正在识别。</summary>
    [Theory]
    [InlineData("zh-CN")]
    [InlineData("en-US")]
    public async Task UploadPending_RendersUploadProgressWithoutRecognition(string culture)
    {
        var (page, state) = CreatePage();
        var owner = "joint-upload-test-" + Guid.NewGuid().ToString("N");
        SetField(page, "_owner", owner);
        var language = (LanguageManager)typeof(JointValidation).GetProperty("Language", Members)!.GetValue(page)!;
        language.SetLanguage(culture);
        using var lifetime = new ValidationFileLifetime();
        typeof(JointValidation).GetProperty("Previews", Members)!.SetValue(page,
            new ImagePreviewStore(new(), lifetime, Microsoft.Extensions.Logging.Abstractions.NullLogger<ImagePreviewStore>.Instance));
        var pipe = new Pipe();
        var retained = Image("retained");
        Field<List<JointValidationImage>>(page, "_images").Add(retained);
        Call(page, "LoadImage", retained);
        Call(page, "PersistState");
        var root = Path.Combine(AppContext.BaseDirectory, "wwwroot", "data", "uploads", owner, "joint-validation");
        try
        {
            var upload = (Task)Call(page, "UploadAsync", new InputFileChangeEventArgs([new JointUploadBrowserFile(pipe.Reader.AsStream(), 1024 * 1024)]))!;
            Assert.False(upload.IsCompleted);
            Assert.True(Field<bool>(page, "_busy"));
            Assert.Equal(retained.Id, Assert.Single(state.Get(owner).Images).Id);
            Assert.Null(typeof(JointValidation).GetField("_identifyTask", Members)!.GetValue(page));
            var frames = RenderFrames(page);
            Assert.DoesNotContain(frames, frame => frame.FrameType == RenderTreeFrameType.Text && frame.TextContent.Contains(language.Translate("JointProcessing")));
            Assert.Contains(frames, frame => frame.FrameType == RenderTreeFrameType.Attribute && frame.AttributeName == "role" && Equals(frame.AttributeValue, "progressbar"));
            Assert.Contains(frames, frame => frame.FrameType == RenderTreeFrameType.Text && frame.TextContent.Contains("slow-image.png"));
            Assert.Contains(frames, frame => frame.FrameType == RenderTreeFrameType.Text && frame.TextContent == language.Translate("Cancel"));
            Call(page, "CancelUpload");
            await upload.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
            Assert.False(Field<bool>(page, "_busy"));
            Assert.Equal(retained.Id, Assert.Single(state.Get(owner).Images).Id);
            Assert.False(Field<CancellationTokenSource>(page, "_cancellation").IsCancellationRequested);
            Assert.Contains(state.Get(owner).Logs, entry => entry.Contains(language.Translate("JointUploadCancelled")));
            var retryPipe = new Pipe();
            try
            {
                var retry = (Task)Call(page, "UploadAsync", new InputFileChangeEventArgs([new JointUploadBrowserFile(retryPipe.Reader.AsStream(), 1024)]))!;
                Assert.False(retry.IsCompleted);
                Call(page, "CancelUpload");
                await retry.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
            }
            finally { await retryPipe.Writer.CompleteAsync(); }
        }
        finally
        {
            await page.DisposeAsync();
            await pipe.Writer.CompleteAsync();
            if (Directory.Exists(root)) { Assert.Empty(Directory.EnumerateFileSystemEntries(root)); Directory.Delete(root); }
            var ownerRoot = Path.GetDirectoryName(root)!;
            if (Directory.Exists(ownerRoot)) { Directory.Delete(ownerRoot); }
        }
        Assert.False(Field<bool>(page, "_busy"));
        Assert.DoesNotContain(RenderFrames(page), frame => frame.FrameType == RenderTreeFrameType.Attribute && frame.AttributeName == "role" && Equals(frame.AttributeValue, "progressbar"));
    }

    /// <summary>直接构建实际页面的 Razor 渲染树，检查真实条件显示，不复制页面实现。</summary>
    private static RenderTreeFrame[] RenderFrames(JointValidation page)
    {
        using var builder = new RenderTreeBuilder();
        typeof(JointValidation).GetMethod("BuildRenderTree", Members)!.Invoke(page, [builder]);
        var frames = builder.GetFrames();
        return frames.Array.Take(frames.Count).ToArray();
    }

    [Fact]
    public async Task ClickWithoutModels_SelectsImageAndPromptsInsteadOfCallingInference()
    {
        var (page, state) = CreatePage();
        var image = Image("second");
        Field<List<JointValidationImage>>(page, "_images").AddRange([Image("first"), image]);
        await (Task)Call(page, "SelectImageAsync", image)!;
        Assert.Equal(image.Id, state.Get("alice").SelectedImageId);
        Assert.Equal(image.Path, Field<string>(page, "_imagePath"));
        Assert.Equal(new LanguageManager().Translate("SelectModel"), Field<string>(page, "_error"));
        Assert.Equal(JointValidationMode.Joint, state.Get("alice").Mode);
        Assert.False(Field<bool>(page, "_busy"));
        var toast = (ToastService)typeof(JointValidation).GetProperty("Toast", Members)!.GetValue(page)!;
        var notice = Assert.Single(toast.Entries);
        Assert.Equal(ToastType.Warning, notice.Type);
        Assert.Equal(new LanguageManager().Translate("SelectModel"), notice.Message);
        await (Task)Call(page, "SelectImageAsync", image)!;
        Assert.NotEqual(notice.Id, Assert.Single(toast.Entries).Id);
        await page.DisposeAsync();
    }

    [Fact]
    public async Task BusyClick_DoesNotChangeImageOrMode()
    {
        var (page, state) = CreatePage();
        var first = Image("first");
        Call(page, "LoadImage", first);
        SetField(page, "_mode", JointValidationMode.AnomalibOnly);
        SetField(page, "_busy", true);
        await (Task)Call(page, "SelectImageAsync", Image("second"))!;
        Assert.Equal(first.Id, Field<string>(page, "_selectedImageId"));
        Assert.Equal(JointValidationMode.AnomalibOnly, Field<JointValidationMode>(page, "_mode"));
        Assert.Empty(state.Get("alice").Images);
        await page.DisposeAsync();
    }

    [Fact]
    public async Task Results_AreIndependent_AndConfigurationChangesInvalidateAllImages()
    {
        var (page, state) = CreatePage();
        var first = Image("first") with { Output = new(null, new([], []), JointValidationMode.YoloOnly, true, 10) };
        var second = Image("second");
        Field<List<JointValidationImage>>(page, "_images").AddRange([first, second]);
        Call(page, "LoadImage", second);
        SetField(page, "_error", "second failed");
        Call(page, "SaveImageResult");
        Call(page, "PersistState");
        var saved = state.Get("alice");
        Assert.Equal(10, saved.Images[0].Output!.YoloMilliseconds);
        Assert.Null(saved.Images[0].Error);
        Assert.Null(saved.Images[1].Output);
        Assert.Equal("second failed", saved.Images[1].Error);
        Call(page, "LoadImage", saved.Images[0]);
        Assert.Equal(10, Field<JointValidationOutput>(page, "_output").YoloMilliseconds);
        Call(page, "ClearResult");
        Assert.All(state.Get("alice").Images, image => { Assert.Null(image.Output); Assert.Null(image.Error); });
        Assert.Equal(first.Id, state.Get("alice").SelectedImageId);
        await page.DisposeAsync();
    }

    [Fact]
    public async Task DeleteImage_CancelPreservesFile_AndConfirmRemovesItOnce()
    {
        var (page, state) = CreatePage();
        var path = Path.Combine(Path.GetTempPath(), "joint-confirm-" + Guid.NewGuid().ToString("N") + ".png");
        using var lifetime = new ValidationFileLifetime();
        var previews = new ImagePreviewStore(new(), lifetime, Microsoft.Extensions.Logging.Abstractions.NullLogger<ImagePreviewStore>.Instance);
        typeof(JointValidation).GetProperty("Previews", Members)!.SetValue(page, previews);
        try
        {
            await File.WriteAllBytesAsync(path, [1, 2, 3], TestContext.Current.CancellationToken);
            var image = Image("delete") with { Path = path };
            Field<List<JointValidationImage>>(page, "_images").Add(image);
            Call(page, "PersistState");
            Call(page, "RequestDeleteImage", image);
            Assert.True(File.Exists(path));
            Assert.Single(state.Get("alice").Images);
            Call(page, "CancelDeleteImage");
            Assert.True(File.Exists(path));
            Call(page, "RequestDeleteImage", image);
            Call(page, "ConfirmDeleteImage");
            Assert.False(File.Exists(path));
            Assert.Empty(state.Get("alice").Images);
            Call(page, "ConfirmDeleteImage");
            Assert.Empty(Field<List<JointValidationImage>>(page, "_images"));
        }
        finally { File.Delete(path); await page.DisposeAsync(); }
    }

    private static (JointValidation Page, JointValidationState State) CreatePage()
    {
        var page = new JointValidation();
        var state = new JointValidationState();
        SetField(page, "_owner", "alice");
        typeof(JointValidation).GetProperty("ValidationState", Members)!.SetValue(page, state);
        typeof(JointValidation).GetProperty("Language", Members)!.SetValue(page, new LanguageManager());
        typeof(JointValidation).GetProperty("Toast", Members)!.SetValue(page, new ToastService());
        return (page, state);
    }

    private static JointValidationImage Image(string id) => new(id, id + ".png", "/" + id + ".png", id + ".png", 32, 32, "/" + id + ".preview.jpg");
    private const BindingFlags Members = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
    private static T Field<T>(JointValidation page, string name) => (T)typeof(JointValidation).GetField(name, Members)!.GetValue(page)!;
    private static void SetField(JointValidation page, string name, object value) => typeof(JointValidation).GetField(name, Members)!.SetValue(page, value);
    private static object? Call(JointValidation page, string name, params object?[] arguments) => typeof(JointValidation).GetMethod(name, Members)!.Invoke(page, arguments);
}
