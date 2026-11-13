using System.Reflection;
using Microsoft.Extensions.Logging.Abstractions;
using Snet.Yolo.Server.anomalib;
using Snet.Yolo.Tasks.Components.Pages;
using Snet.Yolo.Tasks.Core.Localization;
using Snet.Yolo.Tasks.Services;
using Xunit;

namespace Snet.Yolo.Test;

/// <summary>执行实际验证页的模型切换，检查文件、结果和刷新状态按模型隔离。</summary>
public sealed class AnomalibValidationModelIsolationTests
{
    /// <summary>点击图片进入现有识别流程；忙碌时拒绝重复切换，视频只切换预览。</summary>
    [Fact]
    public async Task SelectingImage_StartsRecognition_WhileBusyAndVideoSelectionDoNot()
    {
        using var lifetime = new ValidationFileLifetime();
        var state = new AnomalibValidationState();
        var page = CreatePage(state, lifetime);
        var factory = new RejectingAnomalibOptionsFactory();
        using var inference = new AnomalibOnnxInference(factory);
        SetProperty(page, "Inference", inference);
        var first = new AnomalibValidationMedia { Path = "first.png", Url = "/first.png", Name = "first", IsVideo = false };
        var second = first with { Path = "second.png", Url = "/second.png", Name = "second" };
        var video = first with { Path = "video.mp4", Url = "/video.mp4", Name = "video", IsVideo = true };
        try
        {
            Call(page, "SelectModel", "project/first");
            Files(page).AddRange([first, second, video]);
            SetField(page, "_selectedFile", first);
            await (Task)Call(page, "SelectFileAsync", second)!;
            Assert.Equal(1, factory.Attempts);
            Assert.Same(second, Field<AnomalibValidationMedia>(page, "_selectedFile"));
            Assert.Equal(second.Url, state.Get("alice").SelectedFileUrl);
            Assert.Contains(state.Get("alice").Logs, log => log.Key == "AnomalibIdentifyingImage");
            Assert.Contains(state.Get("alice").Logs, log => log.Arguments.Contains("受控识别入口检查"));
            Assert.False(Field<bool>(page, "_busy"));

            SetField(page, "_busy", true);
            await (Task)Call(page, "SelectFileAsync", first)!;
            Assert.Same(second, Field<AnomalibValidationMedia>(page, "_selectedFile"));
            Assert.Equal(1, factory.Attempts);
            SetField(page, "_busy", false);
            await (Task)Call(page, "SelectFileAsync", video)!;
            Assert.Same(video, Field<AnomalibValidationMedia>(page, "_selectedFile"));
            Assert.Equal(1, factory.Attempts);
            await (Task)Call(page, "SelectFileAsync", first with { Url = "/foreign.png" })!;
            Assert.Same(video, Field<AnomalibValidationMedia>(page, "_selectedFile"));
        }
        finally { await page.DisposeAsync(); }
    }

    /// <summary>切换模型不能继承其他模型的图片；切回或重新进入时恢复自己的结果及选中图片。</summary>
    [Fact]
    public async Task SwitchingModels_RestoresOnlyTheirOwnMediaResultsAndSelection()
    {
        var root = Path.Combine(Path.GetTempPath(), "anomalib-model-state-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        using var lifetime = new ValidationFileLifetime();
        var state = new AnomalibValidationState();
        var page = CreatePage(state, lifetime);
        AnomalibValidation? refreshed = null;
        try
        {
            var first = await MediaAsync(root, "first");
            var second = await MediaAsync(root, "second");
            Call(page, "SelectModel", "project/first");
            Files(page).Add(first);
            SetField(page, "_selectedFile", first);
            Call(page, "PersistState");
            Call(page, "SelectModel", "project/second");
            Assert.Empty(Files(page));
            Assert.Null(Field<AnomalibValidationMedia?>(page, "_selectedFile"));
            Files(page).Add(second);
            SetField(page, "_selectedFile", second);
            Call(page, "PersistState");

            Call(page, "SelectModel", "project/first");
            var restored = Assert.Single(Files(page));
            Assert.Equal(first.Url, restored.Url);
            Assert.Equal(first.Url, Field<AnomalibValidationMedia>(page, "_selectedFile").Url);
            Assert.Equal(0.8f, restored.Output!.Result.ImageScore);
            restored.Output = null;
            Call(page, "PersistState");
            Call(page, "SelectModel", "project/second");
            Assert.Equal(second.Url, Assert.Single(Files(page)).Url);
            Assert.NotNull(Assert.Single(Files(page)).Output);

            refreshed = CreatePage(state, lifetime);
            Call(refreshed, "SelectModel", state.Get("alice").SelectedModelKey);
            Assert.Equal(second.Url, Assert.Single(Files(refreshed)).Url);
            Assert.NotNull(Assert.Single(Files(refreshed)).Output);
            Assert.Empty(state.Get("bob").Files);
            SetField(page, "_pendingMediaDelete", Assert.Single(Files(page)));
            Call(page, "ConfirmRemoveMedia");
            Assert.Empty(Files(page));
            Assert.False(File.Exists(second.Path));
            Assert.True(File.Exists(first.Path));
            Call(page, "SelectModel", "project/first");
            Assert.Equal(first.Url, Assert.Single(Files(page)).Url);
            Call(page, "SelectModel", "project/second");
            Assert.Empty(Files(page));
            Call(page, "SelectDeletedModel");
            Assert.Empty(Files(page));
            Call(page, "SelectModel", "project/first");
            Assert.Equal(first.Url, Assert.Single(Files(page)).Url);
        }
        finally
        {
            await page.DisposeAsync();
            if (refreshed is not null) { await refreshed.DisposeAsync(); }
            foreach (var file in Directory.EnumerateFiles(root)) { File.Delete(file); }
            Directory.Delete(root);
        }
    }

    /// <summary>构造无需模型加载和数据库访问的页面，仍调用产品的切换与持久化方法。</summary>
    private static AnomalibValidation CreatePage(AnomalibValidationState state, ValidationFileLifetime lifetime)
    {
        var page = new AnomalibValidation();
        SetField(page, "_owner", "alice");
        SetField(page, "_models", new[] { Model("first"), Model("second") });
        SetProperty(page, "Language", new LanguageManager());
        SetProperty(page, "ValidationState", state);
        SetProperty(page, "Previews", new ImagePreviewStore(new() { Enabled = false }, lifetime, NullLogger<ImagePreviewStore>.Instance));
        SetProperty(page, "FileLifetime", lifetime);
        return page;
    }

    /// <summary>创建不同模型的源文件占位及结果；测试不执行推理。</summary>
    private static async Task<AnomalibValidationMedia> MediaAsync(string root, string name)
    {
        var path = Path.Combine(root, name + ".png");
        await File.WriteAllBytesAsync(path, [1], TestContext.Current.CancellationToken);
        return new()
        {
            Name = name, Path = path, Url = "/uploads/alice/anomalib-validation/" + name + ".png", IsVideo = false,
            Output = new()
            {
                HeatmapDataUrl = null,
                Result = new() { ImageScore = 0.8f, IsAnomalous = true, OriginalWidth = 32, OriginalHeight = 32, MapWidth = 1, MapHeight = 1, InferenceMilliseconds = 2, Regions = [] },
            },
        };
    }

    /// <summary>构造只用于页面选择的模型元数据。</summary>
    private static RegisteredAnomalibModel Model(string run) => new()
    {
        ProjectId = "project", RunId = run, Model = AnomalibModelKind.Padim,
        OnnxPath = run + ".onnx", ManifestPath = run + ".json", RegisteredAtUtc = DateTime.UnixEpoch,
    };

    /// <summary>测试访问实际页面状态与事件方法所用的成员范围。</summary>
    private const BindingFlags Members = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    /// <summary>读取当前模型的文件队列。</summary>
    private static List<AnomalibValidationMedia> Files(AnomalibValidation page) => Field<List<AnomalibValidationMedia>>(page, "_files");
    /// <summary>读取实际页面的指定字段。</summary>
    private static T Field<T>(AnomalibValidation page, string name) => (T)typeof(AnomalibValidation).GetField(name, Members)!.GetValue(page)!;
    /// <summary>为事件回归准备页面字段。</summary>
    private static void SetField(AnomalibValidation page, string name, object? value) => typeof(AnomalibValidation).GetField(name, Members)!.SetValue(page, value);
    /// <summary>注入测试所需的真实页面服务。</summary>
    private static void SetProperty(AnomalibValidation page, string name, object value) => typeof(AnomalibValidation).GetProperty(name, Members)!.SetValue(page, value);
    /// <summary>执行实际页面事件方法。</summary>
    private static object? Call(AnomalibValidation page, string method, params object?[] args) => typeof(AnomalibValidation).GetMethod(method, Members)!.Invoke(page, args);
}
