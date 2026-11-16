using System.IO.Compression;
using System.IO.Pipelines;
using System.Text;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.Extensions.Logging.Abstractions;
using SkiaSharp;
using Snet.Yolo.Server;
using Snet.Yolo.Server.models.data;
using Snet.Yolo.Tasks.Core.Localization;
using Snet.Yolo.Tasks.Core.Models;
using Snet.Yolo.Tasks.Core.Serialization.Export;
using Snet.Yolo.Tasks.Core.Workspace;
using Snet.Yolo.Tasks.Services;
using Xunit;

namespace Snet.Yolo.Test;

/// <summary>执行上传中心的实际 ZIP 接收、解压与数据库提交，验证新上限和失败回滚。</summary>
[Collection("Database")]
public sealed class YoloArchiveUploadTests
{
    /// <summary>小型真实图片包贯通实际导入流程；读取入口使用 128 GiB 上限，取消和坏图不能留下半批任务。</summary>
    [Theory]
    [InlineData("success")]
    [InlineData("invalid-image")]
    [InlineData("cancel")]
    public async Task ArchiveUpload_PreservesDataAndUses128GiBReadLimit(string mode)
    {
        var token = TestContext.Current.CancellationToken;
        var owner = "archive-upload-" + Guid.NewGuid().ToString("N");
        await using var projects = new ProjectOperate(owner);
        await using var tasks = new ProjectTaskOperate(owner);
        var user = new CurrentUserContext(new UploadTestAuthentication(owner));
        var workspace = new WorkspaceService(projects, tasks, null!, user, NullLogger<WorkspaceService>.Instance);
        var project = new WorkspaceProject
        {
            LabelConfigXml = "<View><Image name='image' value='$image'/><RectangleLabels name='box' toName='image'><Label value='existing'/></RectangleLabels></View>",
            Tasks = [new() { Id = 1, Data = new() { ["image"] = "existing.png" } }],
        };
        var originalConfig = project.LabelConfigXml;
        await workspace.SaveProjectAsync(project, token);
        var root = (await workspace.GetProjectUploadLocationAsync(project.Id)).Directory;
        Directory.CreateDirectory(root);
        using var lifetime = new ValidationFileLifetime();
        var previews = new ImagePreviewStore(new() { Enabled = false }, lifetime, NullLogger<ImagePreviewStore>.Instance);
        using var uploads = new UploadCenter(workspace, null!, new(), new(), user, new LanguageManager(), null!, previews,
            NullLogger<UploadCenter>.Instance, new(), new());
        using var bitmap = new SKBitmap(32, 24);
        bitmap.Erase(SKColors.CornflowerBlue);
        using var encoded = bitmap.Encode(SKEncodedImageFormat.Png, 100);
        var image = encoded.ToArray();
        var bytes = ZipHelper.Pack([
            new("classes.txt", Encoding.UTF8.GetBytes("0 part\n")),
            new("images/1.png", image), new("labels/1.txt", Encoding.UTF8.GetBytes("0 0.5 0.5 0.5 0.5\n")),
            new("images/2.png", mode == "invalid-image" ? new byte[] { 1, 2, 3 } : image), new("labels/2.txt", []),
        ]);
        var pipe = new Pipe();
        var file = new YoloArchiveBrowserFile(mode == "cancel" ? pipe.Reader.AsStream() : new MemoryStream(bytes), bytes.Length);
        var scope = "project:" + project.Id;
        uploads.Arm(new(UploadKind.ProjectYoloArchive, scope, project.Id));
        try
        {
            await uploads.OnArchiveSelectedAsync(new InputFileChangeEventArgs([file]));
            await WaitForAsync(() => file.RequestedLimit > 0 || uploads.GetJob(scope)?.IsRunning == false);
            Assert.Equal(128L * 1024 * 1024 * 1024, file.RequestedLimit);
            Assert.True(file.ReadCancellation.CanBeCanceled);
            if (mode == "cancel") { uploads.Cancel(scope); }
            await WaitForAsync(() => uploads.GetJob(scope)?.IsRunning == false);
            var saved = Assert.IsType<WorkspaceProject>(await workspace.GetProjectAsync(project.Id, token));
            if (mode == "success")
            {
                Assert.Equal(UploadPhase.Completed, uploads.GetJob(scope)!.Phase);
                Assert.Equal(3, saved.Tasks.Count);
                Assert.Equal("existing.png", saved.Tasks[0].Data!["image"]!.GetValue<string>());
                var row = Assert.Single(Assert.Single(saved.Tasks[1].Annotations).Result);
                Assert.Equal(32, row.OriginalWidth); Assert.Equal(24, row.OriginalHeight);
                Assert.Equal("part", row.Value!["rectanglelabels"]![0]!.GetValue<string>());
                Assert.Empty(Assert.Single(saved.Tasks[2].Annotations).Result);
                Assert.Equal(2, Directory.GetFiles(root, "*.png").Length);
                Assert.Empty(Directory.GetFiles(root, "*.upload"));
            }
            else
            {
                Assert.Equal(mode == "cancel" ? UploadPhase.Cancelled : UploadPhase.Failed, uploads.GetJob(scope)!.Phase);
                Assert.Single(saved.Tasks);
                Assert.Equal(originalConfig, saved.LabelConfigXml);
                Assert.Empty(Directory.EnumerateFileSystemEntries(root));
            }
        }
        finally
        {
            uploads.Cancel(scope);
            await pipe.Writer.CompleteAsync();
            await WaitForAsync(() => !uploads.Busy);
            var query = await projects.QueryAsync(owner, project.Id, CancellationToken.None);
            if (query.GetDetails(out List<ProjectData>? rows) && rows is { Count: > 0 })
            {
                await projects.DeleteAggregateAsync(rows[0].id, owner, project.Id, CancellationToken.None);
            }
            if (Directory.Exists(root)) { Directory.Delete(root, recursive: true); }
        }
    }

    /// <summary>等待可观察状态，不用固定延时猜测上传阶段。</summary>
    private static async Task WaitForAsync(Func<bool> condition)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(20));
        while (!condition()) { await Task.Delay(20, timeout.Token); }
    }
}
