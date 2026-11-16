using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;
using Snet.Yolo.Tasks.Core.Serialization.Export;
using Snet.Yolo.Tasks.Core.Serialization.Import;
using Snet.Yolo.Tasks.Services;
using Xunit;

namespace Snet.Yolo.Test;

/// <summary>检查大型 ZIP 的解压后总量、取消及半文件清理，不在开发机生成上百 GiB 文件。</summary>
public sealed class YoloLargeImportTests
{
    /// <summary>按 64 位容量检查磁盘，不创建测试用大文件；不足时必须明确拒绝。</summary>
    [Fact]
    public void DiskSpaceGuard_RejectsInsufficientSpaceWithoutAllocatingFile()
    {
        var path = Path.GetTempPath();
        var volume = OperatingSystem.IsWindows() ? Path.GetPathRoot(path)! : path;
        var required = new DriveInfo(volume).TotalSize + 1024L * 1024 * 1024;
        var method = typeof(UploadCenter).GetMethod("EnsureFreeSpace", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)!;
        var exception = Assert.Throws<System.Reflection.TargetInvocationException>(() => method.Invoke(null, [path, required, "解压目标目录"]));
        Assert.Contains("解压目标目录可用空间不足", Assert.IsType<InvalidDataException>(exception.InnerException).Message);
    }

    /// <summary>128 GiB 压缩包可能解压出更多图片；2000 张上限图片约 195 GiB，应可通过总量自检。</summary>
    [Theory]
    [InlineData(2000, true)]
    [InlineData(2700, false)]
    public void Inspect_Uses256GiBExpandedImageBudget(int imageCount, bool accepted)
    {
        // 只改中央目录声明的解压大小来测试资源闸门；图片真实内容仍由导入时的解码器单独验证。
        var files = new List<ExportFile> { new("classes.txt", Encoding.UTF8.GetBytes("0 part\n")) };
        files.AddRange(Enumerable.Range(1, imageCount).Select(index => new ExportFile($"images/{index}.png", [1])));
        var bytes = ZipHelper.Pack(files);
        var central = (int)BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(bytes.Length - 6, 4));
        while (BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(central, 4)) == 0x02014b50)
        {
            var nameLength = BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(central + 28, 2));
            var extraLength = BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(central + 30, 2));
            var commentLength = BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(central + 32, 2));
            var name = Encoding.UTF8.GetString(bytes, central + 46, nameLength);
            if (name.StartsWith("images/", StringComparison.Ordinal))
            {
                BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(central + 24, 4), (uint)YoloWithImagesImporter.MaximumImageBytes);
            }
            central += 46 + nameLength + extraLength + commentLength;
        }
        using var input = new MemoryStream(bytes);
        using var archive = new ZipArchive(input, ZipArchiveMode.Read);
        if (accepted) { Assert.Equal(imageCount, YoloWithImagesImporter.Inspect(archive).Images.Count); }
        else { Assert.Contains("256 GiB", Assert.Throws<InvalidDataException>(() => YoloWithImagesImporter.Inspect(archive)).Message); }
    }

    /// <summary>取消后不能继续遍历和解析大型 ZIP。</summary>
    [Fact]
    public void Inspect_HonorsCancellation()
    {
        using var token = new CancellationTokenSource();
        token.Cancel();
        using var input = new MemoryStream(ZipHelper.Pack([new("classes.txt", [1])]));
        using var archive = new ZipArchive(input, ZipArchiveMode.Read);
        Assert.ThrowsAny<OperationCanceledException>(() => YoloWithImagesImporter.Inspect(archive, token.Token));
        Assert.ThrowsAny<OperationCanceledException>(() => YoloWithImagesImporter.Inspect("not-opened.zip", token.Token));
    }

    /// <summary>图片传输进行到一半取消，实际解压方法必须停止并删除 .upload 半文件。</summary>
    [Fact]
    public async Task ImageExtraction_CancellationRemovesPartialFile()
    {
        using var packed = new MemoryStream();
        using (var zip = new ZipArchive(packed, ZipArchiveMode.Create, leaveOpen: true))
        {
            using var entry = zip.CreateEntry("images/large.bmp", CompressionLevel.NoCompression).Open();
            entry.Write(new byte[256 * 1024]);
        }
        using var source = new BlockingArchiveStream(packed.ToArray());
        using var archive = new ZipArchive(source, ZipArchiveMode.Read);
        var root = Path.Combine(Path.GetTempPath(), "yolo-extraction-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        var method = typeof(UploadCenter).GetMethod("StoreArchiveImageAsync", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)!;
        var task = (Task)method.Invoke(null, [archive.Entries[0], "large.bmp", root, cancellation.Token])!;
        try
        {
            await source.DataReadStarted.Task.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
            Assert.Single(Directory.GetFiles(root, "*.upload"));
            cancellation.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => task.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken));
            Assert.Empty(Directory.EnumerateFileSystemEntries(root));
        }
        finally { cancellation.Cancel(); try { await task; } catch (OperationCanceledException) { } Directory.Delete(root, recursive: true); }
    }
}
