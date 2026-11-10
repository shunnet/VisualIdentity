using System.IO.Compression;

namespace Snet.Yolo.Tasks.Services;

/// <summary>解压 FFmpeg 压缩包：把 bin/ffmpeg(.exe) 与 bin/ffprobe(.exe) 平铺到目标目录。</summary>
public static class FfmpegArchiveExtractor
{
    /// <summary>解压所需可执行文件，返回是否两个文件都解压成功。</summary>
    /// <param name="archivePath">zip 路径。</param>
    /// <param name="targetDirectory">目标目录（不存在会自动创建）。</param>
    /// <param name="onFile">每解压一个文件回调一次（文件名）。</param>
    public static bool Extract(string archivePath, string targetDirectory, Action<string>? onFile = null)
    {
        Directory.CreateDirectory(targetDirectory);
        var wanted = OperatingSystem.IsWindows() ? new[] { "ffmpeg.exe", "ffprobe.exe" } : new[] { "ffmpeg", "ffprobe" };
        var extracted = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        using var archive = ZipFile.OpenRead(archivePath);
        foreach (var entry in archive.Entries)
        {
            var name = Path.GetFileName(entry.FullName);
            if (string.IsNullOrEmpty(name) || !wanted.Contains(name, StringComparer.OrdinalIgnoreCase)) { continue; }
            if (entry.FullName.Contains("/bin/", StringComparison.OrdinalIgnoreCase) || entry.FullName.StartsWith("bin/", StringComparison.OrdinalIgnoreCase))
            {
                var target = Path.Combine(targetDirectory, name);
                entry.ExtractToFile(target, overwrite: true);
                extracted.Add(name);
                onFile?.Invoke(name);
            }
        }
        return wanted.All(extracted.Contains);
    }
}
