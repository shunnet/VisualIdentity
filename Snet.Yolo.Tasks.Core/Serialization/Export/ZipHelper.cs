
namespace Snet.Yolo.Tasks.Core.Serialization.Export;

using System.Collections.Generic;
using System.IO;
using System.IO.Compression;

/// <summary>导出 zip 打包工具。</summary>
public static class ZipHelper
{
    /// <summary>异步写入 ZIP64 兼容的输出流；不缓存完整图片或压缩包，且不关闭调用方输出。</summary>
    /// <param name="files">标注内容及原图的惰性文件入口。</param>
    /// <param name="destination">HTTP 响应或文件流，可不支持定位及同步写入。</param>
    /// <param name="cancellationToken">浏览器取消下载时中断文件读取和响应写入。</param>
    public static async Task WriteAsync(IEnumerable<ExportFile> files, Stream destination, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(files);
        ArgumentNullException.ThrowIfNull(destination);
        await using var archive = await ZipArchive.CreateAsync(destination, ZipArchiveMode.Create, leaveOpen: true,
            entryNameEncoding: System.Text.Encoding.UTF8, cancellationToken);
        foreach (var file in files)
        {
            cancellationToken.ThrowIfCancellationRequested();
            // 原图直接存储，避免对 JPEG/PNG 等已压缩数据重复编码；小型标注文件使用快速压缩。
            var entry = archive.CreateEntry(file.Path.Replace('\\', '/'), file.OpenRead is null ? CompressionLevel.Fastest : CompressionLevel.NoCompression);
            await using var source = file.OpenRead?.Invoke() ?? new MemoryStream(file.Content, writable: false);
            await using var target = await entry.OpenAsync(cancellationToken);
            await source.CopyToAsync(target, 128 * 1024, cancellationToken);
        }
    }

    /// <summary>小型导出的内存打包接口；大型数据集和 HTTP 下载使用 WriteAsync。</summary>
    public static byte[] Pack(IEnumerable<ExportFile> files)
    {
        using var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var file in files)
            {
                var entryName = file.Path.Replace('\\', '/');
                var entry = archive.CreateEntry(entryName);
                using var entryStream = entry.Open();
                using var source = file.OpenRead?.Invoke() ?? new MemoryStream(file.Content, writable: false);
                source.CopyTo(entryStream);
            }
        }
        return stream.ToArray();
    }
}
