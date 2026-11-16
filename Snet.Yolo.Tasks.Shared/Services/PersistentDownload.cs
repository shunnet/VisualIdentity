using System.Text.Json.Serialization;

namespace Snet.Yolo.Tasks.Services;

/// <summary>后台生成且可断点读取的下载产物记录。</summary>
public sealed class PersistentDownload
{
    /// <summary>随机传输标识。</summary>
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    /// <summary>认证所有者。</summary>
    public string Owner { get; set; } = "";
    /// <summary>浏览器的幂等请求标识。</summary>
    public string RequestId { get; set; } = "";
    /// <summary>用于避免请求标识被复用于另一个资源。</summary>
    public string Resource { get; set; } = "";
    /// <summary>preparing、ready、failed 或 cancelled。</summary>
    public string State { get; set; } = "preparing";
    /// <summary>下载文件名。</summary>
    public string FileName { get; set; } = "download";
    /// <summary>响应内容类型。</summary>
    public string ContentType { get; set; } = "application/octet-stream";
    /// <summary>生成失败原因。</summary>
    public string? Error { get; set; }
    /// <summary>失败时保留资源不存在或参数无效的 HTTP 语义。</summary>
    public int StatusCode { get; set; } = 400;
    /// <summary>最近访问时间。</summary>
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    /// <summary>生成任务，不关联页面请求取消。</summary>
    [JsonIgnore] public Task Completion { get; set; } = Task.CompletedTask;
    /// <summary>生成时的显式取消源。</summary>
    [JsonIgnore] public CancellationTokenSource Cancellation { get; } = new();
}
