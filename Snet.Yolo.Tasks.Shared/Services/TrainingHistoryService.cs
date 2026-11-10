namespace Snet.Yolo.Tasks.Services;

using Snet.Yolo.Server.anomalib;
using Snet.Yolo.Server.models;
using Snet.Yolo.Tasks.Core.History;

/// <summary>训练指标页的后端：读取汇总、删除训练记录及其产物、打开可导出的模型文件。</summary>
/// <param name="store">训练记录存储。</param>
/// <param name="yolo">YOLO 训练服务，负责把 best.pt 导出为 ONNX。</param>
/// <param name="registry">Anomalib 模型注册表，负责校验并打开训练产物。</param>
/// <param name="logger">日志记录器；省略时不输出日志。</param>
public sealed class TrainingHistoryService(TrainingHistoryStore store, TrainingService yolo, AnomalibModelRegistry registry, Microsoft.Extensions.Logging.ILogger<TrainingHistoryService>? logger = null)
{
    /// <summary>读取一个工程的全部训练记录与汇总；模型文件已被手工删除的记录不再带模型路径，也不参与“最佳”评选。</summary>
    /// <param name="kind">工程算法族。</param>
    /// <param name="owner">当前登录用户。</param>
    /// <param name="projectId">工程标识。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>记录与汇总。</returns>
    public async Task<TrainingHistoryView> LoadAsync(ProjectKind kind, string owner, string projectId, CancellationToken cancellationToken = default)
    {
        var records = (await store.ListAsync(kind, owner, projectId, cancellationToken))
            .Select(record => record.HasModel && !ModelExists(kind, owner, projectId, record) ? record with { ModelPath = string.Empty, ModelBytes = 0 } : record)
            .ToArray();
        return new TrainingHistoryView(records, TrainingHistoryAnalyzer.Summarize(records));
    }

    /// <summary>删除一次训练的记录与产物：YOLO 删除该次 runs 输出目录，Anomalib 删除整个运行目录（同时移出验证页模型列表）。</summary>
    /// <param name="kind">工程算法族。</param>
    /// <param name="owner">当前登录用户。</param>
    /// <param name="projectId">工程标识。</param>
    /// <param name="runId">训练运行标识。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>删除结果。</returns>
    public async Task<TrainingDeleteResult> DeleteAsync(ProjectKind kind, string owner, string projectId, string runId, CancellationToken cancellationToken = default)
    {
        var record = await store.FindAsync(kind, owner, projectId, runId, cancellationToken);
        if (record is null) { return TrainingDeleteResult.NotFound; }
        try
        {
            var directory = ResolveRunDirectory(kind, owner, projectId, record);
            if (directory is not null && Directory.Exists(directory)) { Directory.Delete(directory, recursive: true); }
            store.DeleteRecord(kind, owner, projectId, runId);
            return TrainingDeleteResult.Deleted;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            logger?.LogWarning(error, "删除训练记录 {RunId} 失败", runId);
            return TrainingDeleteResult.Failed;
        }
    }

    /// <summary>导出一次 YOLO 训练的 ONNX（页面按钮先调用它显示进度，成功后再下载）；已导出过则直接复用。</summary>
    /// <param name="owner">当前登录用户。</param>
    /// <param name="projectId">工程标识。</param>
    /// <param name="runId">训练运行标识。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>是否成功及说明。</returns>
    public async Task<(bool Ok, string Message)> ExportYoloOnnxAsync(string owner, string projectId, string runId, CancellationToken cancellationToken = default)
    {
        var record = await store.FindAsync(ProjectKind.Yolo, owner, projectId, runId, cancellationToken);
        if (record is null) { return (false, "训练记录不存在。"); }
        var (ok, message, _) = await yolo.ExportRunOnnxAsync(owner, projectId, record, cancellationToken);
        return (ok, message);
    }

    /// <summary>
    /// 打开一次训练的可导出文件。YOLO：<c>pt</c> 为 best.pt，<c>onnx</c> 在需要时先导出；
    /// Anomalib：<c>package</c> 为 ONNX 加清单的 ZIP，<c>onnx</c> 为单个模型文件，<c>report</c> 为逐图校准报告。
    /// </summary>
    /// <param name="kind">工程算法族。</param>
    /// <param name="owner">当前登录用户。</param>
    /// <param name="projectId">工程标识。</param>
    /// <param name="runId">训练运行标识。</param>
    /// <param name="format">导出格式。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>下载流；记录、模型或格式不存在时为空。</returns>
    public async Task<TrainingDownload?> OpenDownloadAsync(ProjectKind kind, string owner, string projectId, string runId, string format, CancellationToken cancellationToken = default)
    {
        var record = await store.FindAsync(kind, owner, projectId, runId, cancellationToken);
        if (record is null || !record.HasModel) { return null; }
        return kind == ProjectKind.Anomalib
            ? await OpenAnomalibAsync(owner, projectId, runId, format, cancellationToken)
            : await OpenYoloAsync(owner, projectId, record, format, cancellationToken);
    }

    /// <summary>YOLO 导出：直接返回 best.pt，或导出（已有则复用）ONNX 后返回。</summary>
    private async Task<TrainingDownload?> OpenYoloAsync(string owner, string projectId, TrainingRunRecord record, string format, CancellationToken cancellationToken)
    {
        string? path;
        try
        {
            if (format == "pt") { path = store.ResolveYoloModel(owner, projectId, record); }
            else if (format == "onnx") { path = (await yolo.ExportRunOnnxAsync(owner, projectId, record, cancellationToken)).Path; }
            else { return null; }
        }
        catch (InvalidDataException) { return null; }
        if (path is null || !File.Exists(path)) { return null; }
        var baseName = System.IO.Path.GetFileNameWithoutExtension(record.Algorithm) + "-" + record.Id;
        return new TrainingDownload(OpenRead(path), baseName + System.IO.Path.GetExtension(path), "application/octet-stream");
    }

    /// <summary>Anomalib 导出：校验清单与模型摘要后返回产物。</summary>
    private async Task<TrainingDownload?> OpenAnomalibAsync(string owner, string projectId, string runId, string format, CancellationToken cancellationToken)
    {
        switch (format)
        {
            case "package":
                return await registry.OpenTrainingPackageDownloadAsync(owner, projectId, runId, cancellationToken) is { } package
                    ? new TrainingDownload(package, runId + ".zip", "application/zip") : null;
            case "onnx":
                return await registry.OpenTrainingModelDownloadAsync(owner, projectId, runId, cancellationToken) is { } model
                    ? new TrainingDownload(model, runId + ".onnx", "application/octet-stream") : null;
            case "report":
                return await registry.OpenTrainingReportDownloadAsync(owner, projectId, runId, cancellationToken) is { } report
                    ? new TrainingDownload(report, runId + "-calibration.json", "application/json") : null;
            default:
                return null;
        }
    }

    /// <summary>该次训练的输出目录是否仍有记录中的模型文件。</summary>
    private bool ModelExists(ProjectKind kind, string owner, string projectId, TrainingRunRecord record)
    {
        try { return File.Exists(kind == ProjectKind.Yolo ? store.ResolveYoloModel(owner, projectId, record) : store.ResolveInProject(kind, owner, projectId, record.ModelPath)); }
        catch (InvalidDataException) { return false; }
    }

    /// <summary>
    /// 解析要删除的输出目录并确认它位于工程训练目录内：
    /// YOLO 必须在 <c>runs</c> 之下（防止记录被篡改后删除数据集等其他目录），Anomalib 必须是 <c>anomalib-run-</c> 运行目录。
    /// </summary>
    private string? ResolveRunDirectory(ProjectKind kind, string owner, string projectId, TrainingRunRecord record)
    {
        if (string.IsNullOrWhiteSpace(record.RunDirectory)) { return null; }
        // 去掉结尾分隔符后再比较：记录里写 "runs/" 会解析成 runs 目录本身，它不是某一次训练的目录，不能被删除。
        var directory = store.ResolveInProject(kind, owner, projectId, record.RunDirectory)
            .TrimEnd(System.IO.Path.DirectorySeparatorChar, System.IO.Path.AltDirectorySeparatorChar);
        var projectDirectory = store.ProjectDirectory(kind, owner, projectId);
        var allowedRoot = kind == ProjectKind.Anomalib ? projectDirectory : System.IO.Path.Combine(projectDirectory, "runs");
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        if (!directory.StartsWith(allowedRoot + System.IO.Path.DirectorySeparatorChar, comparison)) { throw new InvalidDataException("训练输出目录不在允许删除的位置。"); }
        // Anomalib 的运行目录必须是工程目录的直接子目录，名称形如 anomalib-run-<标识>。
        if (kind == ProjectKind.Anomalib
            && (!string.Equals(System.IO.Path.GetDirectoryName(directory), projectDirectory, comparison)
                || !System.IO.Path.GetFileName(directory).StartsWith("anomalib-run-", StringComparison.Ordinal)))
        {
            throw new InvalidDataException("不是 Anomalib 运行目录。");
        }
        return directory;
    }

    /// <summary>以顺序异步读取方式打开文件，允许下载期间文件被删除。</summary>
    private static FileStream OpenRead(string path)
        => new(path, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete, 1024 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
}
