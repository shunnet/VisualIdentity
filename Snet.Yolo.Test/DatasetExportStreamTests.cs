using System.IO.Compression;
using System.Text.Json.Nodes;
using Snet.Yolo.Tasks.Core.Config;
using Snet.Yolo.Tasks.Core.Models;
using Snet.Yolo.Tasks.Core.Serialization.Export;
using Xunit;

namespace Snet.Yolo.Test;

/// <summary>大数据集必须流式写出 ZIP64，保持标签、空标注和分类结构，并正确响应取消。</summary>
public sealed class DatasetExportStreamTests
{
    /// <summary>流式清单不持有图片字节；实际 ZIP 的文件内容与既有导出一致。</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task YoloWithImageFiles_PreservesLegacyContentsWithoutLoadingImages(bool classify)
    {
        var config = LabelingConfigParser.Parse(classify
            ? "<View><Image name='image' value='$image'/><Choices name='class' toName='image'><Choice value='正常'/></Choices></View>"
            : "<View><Image name='image' value='$image'/><RectangleLabels name='box' toName='image'><Label value='缺陷'/></RectangleLabels></View>");
        AnnotationTask[] tasks = [new() { Data = new() { ["image"] = "one.png", ["class"] = "正常" } }, new() { Data = new() { ["image"] = "two.png", ["class"] = "正常" } }];
        if (!classify)
        {
            tasks[0].Annotations = [new() { Result = [new() { Type = RegionType.RectangleLabels,
                Value = new() { ["x"] = 10, ["y"] = 20, ["width"] = 30, ["height"] = 40, ["rectanglelabels"] = new JsonArray("缺陷") } }] }];
        }
        var path = Path.Combine(Path.GetTempPath(), "export-stream-" + Guid.NewGuid().ToString("N") + ".png");
        var secondPath = path + ".second.png";
        var bytes = new byte[] { 1, 2, 3, 4, 5 };
        var secondBytes = bytes.Reverse().ToArray();
        await File.WriteAllBytesAsync(path, bytes, TestContext.Current.CancellationToken);
        await File.WriteAllBytesAsync(secondPath, secondBytes, TestContext.Current.CancellationToken);
        try
        {
            var legacy = ExportService.YoloWithImages(tasks, config, image => image == "one.png" ? bytes : secondBytes);
            var streamed = ExportService.YoloWithImageFiles(tasks, config, image => image == "one.png" ? path : secondPath);
            Assert.All(streamed.Files.Where(file => file.OpenRead is not null), file => Assert.Empty(file.Content));
            Assert.Equal(2, streamed.Files.Count(file => file.OpenRead is not null));
            using var output = new MemoryStream();
            await ZipHelper.WriteAsync(streamed.Files, output, TestContext.Current.CancellationToken);
            output.Position = 0;
            using var archive = new ZipArchive(output, ZipArchiveMode.Read, leaveOpen: true);
            Assert.Equal(legacy.Files.Count, archive.Entries.Count);
            foreach (var expected in legacy.Files)
            {
                var entry = Assert.IsType<ZipArchiveEntry>(archive.GetEntry(expected.Path));
                using var content = new MemoryStream();
                await using var source = await entry.OpenAsync(TestContext.Current.CancellationToken);
                await source.CopyToAsync(content, TestContext.Current.CancellationToken);
                Assert.Equal(expected.Content, content.ToArray());
            }
        }
        finally { File.Delete(path); File.Delete(secondPath); }
    }

    /// <summary>真实流经 50 GiB 数据但不保存海量内容；检查 ZIP64 的条目长度、归档偏移和有界写入。</summary>
    [Fact]
    public async Task WriteAsync_StreamsFiftyGiBToNonSeekableAsyncOnlyOutputWithZip64()
    {
        const long size = 50L * 1024 * 1024 * 1024;
        var source = new GeneratedExportReadStream(size);
        using var output = new ExportCaptureStream();
        await ZipHelper.WriteAsync([new("images/1.bmp", []) { OpenRead = () => source }], output, TestContext.Current.CancellationToken);
        Assert.True(source.Disposed);
        Assert.True(output.Written > size);
        Assert.InRange(output.MaximumWrite, 1, 128 * 1024);
        using var readable = output.OpenArchive();
        using var archive = new ZipArchive(readable, ZipArchiveMode.Read);
        var entry = Assert.Single(archive.Entries);
        Assert.Equal(size, entry.Length);
        Assert.Equal(size, entry.CompressedLength);
        Assert.Equal("images/1.bmp", entry.FullName);
    }

    /// <summary>取消下载立即终止源流读取并释放文件句柄，不继续打开后续图片。</summary>
    [Fact]
    public async Task WriteAsync_CancellationClosesCurrentImageAndDoesNotOpenNext()
    {
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        var source = new GeneratedExportReadStream(50L * 1024 * 1024 * 1024, reads => { if (reads == 3) { cancellation.Cancel(); } });
        var nextOpened = false;
        using var output = new ExportCaptureStream();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => ZipHelper.WriteAsync([
            new("images/1.bmp", []) { OpenRead = () => source },
            new("images/2.bmp", []) { OpenRead = () => { nextOpened = true; return new MemoryStream(); } },
        ], output, cancellation.Token));
        Assert.True(source.Disposed);
        Assert.False(nextOpened);
        Assert.InRange(output.Written, 0, 1024 * 1024);
    }

    /// <summary>大量图片会超过传统 ZIP 的 65535 条目录限制，ZIP64 必须保留全部条目。</summary>
    [Fact]
    public async Task WriteAsync_SupportsMoreThan65535Entries()
    {
        using var output = new MemoryStream();
        await ZipHelper.WriteAsync(Enumerable.Range(1, 65_536).Select(index => new ExportFile($"labels/{index}.txt", [])),
            output, TestContext.Current.CancellationToken);
        output.Position = 0;
        using var archive = new ZipArchive(output, ZipArchiveMode.Read, leaveOpen: true);
        Assert.Equal(65_536, archive.Entries.Count);
        Assert.NotNull(archive.GetEntry("labels/65536.txt"));
    }
}
