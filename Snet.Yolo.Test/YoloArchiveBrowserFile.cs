using Microsoft.AspNetCore.Components.Forms;

namespace Snet.Yolo.Test;

/// <summary>可控 ZIP 浏览器文件，记录实际读取时的上限和取消令牌，不创建巨型磁盘文件。</summary>
internal sealed class YoloArchiveBrowserFile(Stream content, long size) : IBrowserFile
{
    /// <inheritdoc/>
    public string Name => "dataset.zip";
    /// <inheritdoc/>
    public DateTimeOffset LastModified => DateTimeOffset.UnixEpoch;
    /// <inheritdoc/>
    public long Size => size;
    /// <inheritdoc/>
    public string ContentType => "application/zip";
    /// <summary>上传中心实际传给 OpenReadStream 的大小上限。</summary>
    public long RequestedLimit { get; private set; }
    /// <summary>用于中止文件读取的实际令牌。</summary>
    public CancellationToken ReadCancellation { get; private set; }
    /// <inheritdoc/>
    public Stream OpenReadStream(long maxAllowedSize = 512000, CancellationToken cancellationToken = default)
    {
        RequestedLimit = maxAllowedSize;
        ReadCancellation = cancellationToken;
        return content;
    }
}
