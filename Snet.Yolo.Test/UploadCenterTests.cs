using Snet.Yolo.Tasks.Services;
using Xunit;

namespace Snet.Yolo.Test;

/// <summary>
/// 上传入口的准入规则与状态判定。
/// 重点：这些规则决定“哪些文件会被读取”，一旦放宽或收紧都会直接影响上传行为，
/// 因此不依赖浏览器环境，用纯函数固定下来。
/// </summary>
public sealed class UploadCenterTests
{
    [Theory]
    [InlineData("photo.jpg")]
    [InlineData("photo.JPEG")]
    [InlineData("photo.png")]
    [InlineData("photo.webp")]
    [InlineData("photo.bmp")]
    [InlineData("photo.gif")]
    public void ProjectImages_AcceptsSupportedImageExtensions(string fileName)
    {
        Assert.True(UploadCenter.IsAcceptable(UploadKind.ProjectImages, fileName, 1024, out var reason), reason);
    }

    [Theory]
    [InlineData("clip.mp4")]
    [InlineData("clip.mkv")]
    public void ProjectImages_RejectsVideoBecauseTrainerOnlyExportsImages(string fileName)
    {
        Assert.False(UploadCenter.IsAcceptable(UploadKind.ProjectImages, fileName, 1024, out _));
    }

    [Theory]
    [InlineData("clip.mp4")]
    [InlineData("clip.webm")]
    public void ValidationImages_AcceptsVideo(string fileName)
    {
        Assert.True(UploadCenter.IsAcceptable(UploadKind.ValidationImages, fileName, 1024, out var reason), reason);
    }

    [Fact]
    public void Images_RejectUnknownExtensionAndOversizedFile()
    {
        Assert.False(UploadCenter.IsAcceptable(UploadKind.ProjectImages, "notes.txt", 1024, out _));
        Assert.False(UploadCenter.IsAcceptable(UploadKind.ProjectImages, "photo.jpg", UploadCenter.MaxImageBytes + 1, out _));
        Assert.False(UploadCenter.IsAcceptable(UploadKind.ProjectImages, "photo.jpg", 0, out _));
        Assert.True(UploadCenter.IsAcceptable(UploadKind.ProjectImages, "photo.jpg", UploadCenter.MaxImageBytes, out _));
    }

    [Fact]
    public void Archive_RequiresZipExtensionAndBoundedSize()
    {
        Assert.True(UploadCenter.IsAcceptable(UploadKind.ProjectYoloArchive, "dataset.zip", 1024, out _));
        Assert.False(UploadCenter.IsAcceptable(UploadKind.ProjectYoloArchive, "dataset.rar", 1024, out _));
        Assert.False(UploadCenter.IsAcceptable(UploadKind.ProjectYoloArchive, "dataset.zip", UploadCenter.MaxArchiveBytes + 1, out _));
    }

    [Fact]
    public void Model_RequiresOnnxExtensionAndRejectsEmptyPayload()
    {
        Assert.True(UploadCenter.IsAcceptable(UploadKind.ValidationModel, "best.onnx", 4096, out _));
        Assert.False(UploadCenter.IsAcceptable(UploadKind.ValidationModel, "best.pt", 4096, out _));
        Assert.False(UploadCenter.IsAcceptable(UploadKind.ValidationModel, "best.onnx", 4, out _));
        Assert.False(UploadCenter.IsAcceptable(UploadKind.ValidationModel, "best.onnx", UploadCenter.MaxOnnxBytes + 1, out _));
    }

    [Theory]
    [InlineData(UploadKind.ProjectClassImages, true)]
    [InlineData(UploadKind.ValidationModel, true)]
    [InlineData(UploadKind.ProjectImages, false)]
    [InlineData(UploadKind.ProjectYoloArchive, false)]
    [InlineData(UploadKind.ValidationImages, false)]
    public void OnlyClassAndModelImportsWaitForConfirmation(UploadKind kind, bool expected)
    {
        Assert.Equal(expected, UploadCenter.RequiresConfirmation(kind));
    }

    [Fact]
    public void ProjectPage_DeclaresAnIntentForEveryUploadEntryPoint()
    {
        var detect = UploadCenter.ProjectPageIntents(isClassify: false, "project:p1", "p1");
        var classify = UploadCenter.ProjectPageIntents(isClassify: true, "project:p1", "p1");

        // 检测工程有「导入图片」与「导入 YOLO ZIP」两个入口，两个都必须声明意图；
        // 漏掉 ZIP 的现场表现就是点按钮只弹一句“操作失败”（上传根本没开始）。
        Assert.Contains(detect, intent => intent.Kind == UploadKind.ProjectImages);
        Assert.Contains(detect, intent => intent.Kind == UploadKind.ProjectYoloArchive);

        // 分类工程没有 ZIP 按钮，图片走“选完再确认”的分类导入。
        Assert.Contains(classify, intent => intent.Kind == UploadKind.ProjectClassImages);
        Assert.DoesNotContain(classify, intent => intent.Kind == UploadKind.ProjectYoloArchive);

        Assert.All(detect.Concat(classify), intent => Assert.Equal("project:p1", intent.ScopeKey));
        Assert.All(detect.Concat(classify), intent => Assert.Equal("p1", intent.ProjectId));
        // 每种上传类型对应一个独立输入槽位，不能重复占用同一个槽位。
        Assert.Equal(detect.Count, detect.Select(intent => intent.Kind).Distinct().Count());
    }

    [Fact]
    public void ValidationPage_DeclaresAnIntentForEveryUploadEntryPoint()
    {
        // ONNX 槽位始终可用（弹窗里选文件）；文件槽位只有选中模型后才能上传。
        var withoutModel = UploadCenter.ValidationPageIntents(-1, "validation:files", "validation:model");
        var withModel = UploadCenter.ValidationPageIntents(7, "validation:files", "validation:model");

        Assert.Contains(withoutModel, intent => intent.Kind == UploadKind.ValidationModel);
        Assert.DoesNotContain(withoutModel, intent => intent.Kind == UploadKind.ValidationImages);

        Assert.Contains(withModel, intent => intent.Kind == UploadKind.ValidationModel);
        var images = Assert.Single(withModel, intent => intent.Kind == UploadKind.ValidationImages);
        Assert.Equal(7, images.ModelIndex);
        Assert.Equal("validation:files", images.ScopeKey);
    }

    [Fact]
    public void LayoutInputElements_HaveDistinctStableIds()
    {
        var ids = new[] { UploadCenter.ImagesInputId, UploadCenter.ArchiveInputId, UploadCenter.OnnxInputId };

        Assert.Equal(3, ids.Distinct(StringComparer.Ordinal).Count());
        Assert.All(ids, id => Assert.False(string.IsNullOrWhiteSpace(id)));
    }

    [Fact]
    public void RunningJob_IsActiveUntilItReachesATerminalPhase()
    {
        var running = new UploadJob("project:p1", UploadKind.ProjectImages, UploadPhase.Running, "上传中", 4, 1, 25, null, DateTime.UtcNow);

        Assert.True(running.IsRunning);
        Assert.False((running with { Phase = UploadPhase.Completed }).IsRunning);
        Assert.False((running with { Phase = UploadPhase.Failed }).IsRunning);
        Assert.False((running with { Phase = UploadPhase.Cancelled }).IsRunning);
    }

    [Fact]
    public void SelectionLimit_IsPositiveAndMatchesConfiguredMaximum()
    {
        Assert.Equal(100, UploadCenter.MaxFilesPerSelection);
    }
}
