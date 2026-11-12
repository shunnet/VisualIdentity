using Microsoft.AspNetCore.Components.Forms;

namespace Snet.Yolo.Test;

/// <summary>为联合验证上传回归提供可由测试控制传输速度的浏览器文件。</summary>
internal sealed class JointUploadBrowserFile(Stream content, long size) : IBrowserFile
{
    /// <summary>受控图片文件名。</summary>
    public string Name => "slow-image.png";

    /// <summary>固定时间避免测试依赖时钟。</summary>
    public DateTimeOffset LastModified => DateTimeOffset.UnixEpoch;

    /// <summary>浏览器声明的总字节数。</summary>
    public long Size => size;

    /// <summary>受控文件的媒体类型。</summary>
    public string ContentType => "image/png";

    /// <summary>返回管道流，由真实页面上传流程读取并响应取消。</summary>
    public Stream OpenReadStream(long maxAllowedSize = 512000, CancellationToken cancellationToken = default) => content;
}
