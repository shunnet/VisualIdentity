
namespace Snet.Yolo.Tasks.Core.Serialization.Export;

using System.Collections.Generic;
using System.IO;
using System.IO.Compression;

/// <summary>导出 zip 打包工具。</summary>
public static class ZipHelper
{
    /// <summary>将多文件打包为 zip（条目使用扁平相对路径，分隔符统一为斜杠）。</summary>
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
                entryStream.Write(file.Content, 0, file.Content.Length);
            }
        }
        return stream.ToArray();
    }
}
