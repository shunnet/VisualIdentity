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
