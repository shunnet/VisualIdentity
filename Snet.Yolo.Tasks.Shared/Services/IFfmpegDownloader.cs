namespace Snet.Yolo.Tasks.Services;

/// <summary>下载 FFmpeg 安装包（抽出来便于单测：测试里用假下载器产出压缩包）。</summary>
public interface IFfmpegDownloader
{
    /// <summary>下载最新版 FFmpeg 压缩包到指定目录。</summary>
    /// <param name="destinationDirectory">压缩包落地目录。</param>
    /// <param name="onProgress">已下载字节数与总字节数（总长未知时为 null）。</param>
    /// <param name="cancellationToken">取消标记。</param>
    /// <returns>压缩包完整路径与版本号。</returns>
    Task<(string ArchivePath, string Version)> DownloadLatestAsync(string destinationDirectory, Action<long, long?> onProgress, CancellationToken cancellationToken);
}
