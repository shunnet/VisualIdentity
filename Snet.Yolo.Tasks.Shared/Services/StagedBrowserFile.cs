using Microsoft.AspNetCore.Components.Forms;

namespace Snet.Yolo.Tasks.Services;

/// <summary>以服务端已完整暂存的文件复用现有导入校验，不再依赖浏览器电路。</summary>
/// <param name="path">可信暂存路径。</param>
/// <param name="file">源文件元数据。</param>
public sealed class StagedBrowserFile(string path, TransferFile file) : IBrowserFile
{
    /// <summary>可信暂存路径，供 ZIP 导入直接复用，避免再复制一份 128 GiB 压缩包。</summary>
    internal string StagedPath => path;
    /// <summary>原始文件名。</summary>
    public string Name => file.Name;
    /// <summary>服务端暂存时间。</summary>
    public DateTimeOffset LastModified => File.GetLastWriteTimeUtc(path);
    /// <summary>经分块接收确认的完整长度。</summary>
    public long Size => file.Size;
    /// <summary>真实内容由既有导入器检测，不信任浏览器 MIME。</summary>
    public string ContentType => "application/octet-stream";
    /// <summary>按原有大小限制打开异步文件流。</summary>
    public Stream OpenReadStream(long maxAllowedSize = 512000, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (Size > maxAllowedSize || new FileInfo(path).Length != Size) { throw new InvalidDataException("暂存文件长度无效。"); }
        return new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 128 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
    }
}
