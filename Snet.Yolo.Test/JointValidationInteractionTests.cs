using System.Reflection;
using Snet.Yolo.Server.anomalib;
using Snet.Yolo.Tasks.Components.Pages;
using Snet.Yolo.Tasks.Core.Localization;
using Snet.Yolo.Tasks.Services;
using Xunit;

namespace Snet.Yolo.Test;

/// <summary>执行联合验证页实际切换与保存方法，不依赖原生模型或运行中的浏览器。</summary>
public sealed class JointValidationInteractionTests
{
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
