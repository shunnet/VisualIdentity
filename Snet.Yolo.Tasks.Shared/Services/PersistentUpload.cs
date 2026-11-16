using System.Text.Json.Serialization;

namespace Snet.Yolo.Tasks.Services;

/// <summary>独立于页面和电路的上传记录；分块偏移与意图持久保存，运行资源不参与序列化。</summary>
public sealed class PersistentUpload
{
    /// <summary>不可预测的任务标识。</summary>
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    /// <summary>可信认证用户名。</summary>
    public string Owner { get; set; } = string.Empty;
    /// <summary>固定的业务目标，刷新后不从当前页面重新推断。</summary>
    public UploadIntent Intent { get; set; } = new(UploadKind.ProjectImages, "");
    /// <summary>创建任务时复制的表单参数。</summary>
    public UploadDraft Draft { get; set; } = new();
    /// <summary>各文件的元数据。</summary>
    public TransferFile[] Files { get; set; } = [];
    /// <summary>各文件已完整写入的字节偏移。</summary>
    public long[] Offsets { get; set; } = [];
    /// <summary>传输、导入、完成、失败或取消状态。</summary>
    public string State { get; set; } = "uploading";
    /// <summary>可诊断的失败原因。</summary>
    public string? Error { get; set; }
    /// <summary>最近活动时间，用于过期清理。</summary>
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    /// <summary>取消后的磁盘清理与写入互斥。</summary>
    [JsonIgnore] public SemaphoreSlim Gate { get; } = new(1, 1);
    /// <summary>后台导入取消源。</summary>
    [JsonIgnore] public CancellationTokenSource Cancellation { get; } = new();
    /// <summary>正在执行的独立上传中心。</summary>
    [JsonIgnore] public UploadCenter? Processor { get; set; }
    /// <summary>终态进度持久保存，释放后台服务引用后仍可显示结果。</summary>
    public UploadJob? LastProgress { get; set; }
    /// <summary>导入阶段的进度快照。</summary>
    [JsonIgnore] public UploadJob? Progress => Processor?.GetJob(Intent.ScopeKey) ?? LastProgress;
}
