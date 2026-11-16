using System.Collections.Concurrent;
using System.Text.Json;
using Microsoft.Net.Http.Headers;
using Snet.Yolo.Server;
using Snet.Yolo.Server.anomalib;
using Snet.Yolo.Server.models;
using Snet.Yolo.Server.models.data;
using Snet.Yolo.Tasks.Core.Serialization.Export;

namespace Snet.Yolo.Tasks.Services;

/// <summary>下载产物独立生成、原子发布、按认证用户读取；刷新后保持同一文件及 ETag。</summary>
public sealed class PersistentDownloadService
{
    private readonly ConcurrentDictionary<string, PersistentDownload> _jobs = new();
    private readonly object _gate = new();
    private readonly IServiceScopeFactory _scopes;
    private readonly ILogger<PersistentDownloadService> _logger;
    private readonly string _root = Path.Combine(AppContext.BaseDirectory, "transfers", "downloads");

    /// <summary>恢复已生成文件；中断的生成明确失败，不把 .part 当作下载产物。</summary>
    public PersistentDownloadService(IServiceScopeFactory scopes, ILogger<PersistentDownloadService> logger)
    {
        _scopes = scopes; _logger = logger; Directory.CreateDirectory(_root);
        foreach (var path in Directory.EnumerateFiles(_root, "*.json"))
        {
            try
            {
                var job = JsonSerializer.Deserialize<PersistentDownload>(File.ReadAllText(path));
                if (job is null || !Guid.TryParseExact(job.Id, "N", out _) || Path.GetFileNameWithoutExtension(path) != job.Id) { continue; }
                if (job.State == "preparing") { job.State = "failed"; job.Error = "服务重启中断下载生成，请重新导出。"; Save(job); }
                _jobs[job.Id] = job;
            }
            catch (Exception error) when (error is IOException or JsonException) { logger.LogWarning(error, "无法读取下载记录"); }
        }
    }

    /// <summary>查询仅属于当前认证用户的任务。</summary>
    public PersistentDownload? Find(string owner, string id) => _jobs.TryGetValue(id, out var job) && job.Owner == owner ? job : null;

    /// <summary>所有下载入口共用的后台生成入口；直接 HTTP 调用也支持 Range。</summary>
    public async Task<IResult> DownloadAsync(HttpContext context, string resource)
    {
        var owner = context.User.Identity?.Name;
        if (string.IsNullOrWhiteSpace(owner)) { return Results.Unauthorized(); }
        var requestId = context.Request.Query["transfer"].ToString();
        if (!Guid.TryParseExact(requestId, "N", out _)) { requestId = Guid.NewGuid().ToString("N"); }
        PersistentDownload job;
        lock (_gate)
        {
            job = _jobs.Values.FirstOrDefault(item => item.Owner == owner && item.RequestId == requestId && item.Resource == resource)!;
            if (job is null)
            {
                if (_jobs.Values.Count(item => item.Owner == owner && item.State == "preparing") >= 2) { return Results.Problem("已有下载正在准备，请稍后重试。", statusCode: 429); }
                CleanupExpired();
                job = new() { Owner = owner, RequestId = requestId, Resource = resource };
                Save(job); _jobs[job.Id] = job;
                job.Completion = Task.Run(() => GenerateAsync(job));
            }
        }
        if (context.Request.Query["persistent"] == "1") { return Results.Ok(View(job)); }
        await job.Completion.WaitAsync(context.RequestAborted);
        return FileResult(context, job);
    }

    /// <summary>稳定的物理文件响应，支持 Range/If-Range，源训练产物不会被下载清理删除。</summary>
    public IResult FileResult(HttpContext context, PersistentDownload job)
    {
        if (job.State != "ready") { return Results.Problem(title: "下载生成失败", detail: job.Error, statusCode: job.StatusCode); }
        var path = PathOf(job);
        if (!File.Exists(path)) { return Results.NotFound(); }
        job.UpdatedAt = DateTime.UtcNow;
        try { Save(job); } catch (IOException error) { _logger.LogWarning(error, "无法更新下载缓存访问时间 {Id}", job.Id); }
        context.Response.Headers.CacheControl = "private, no-store";
        context.Response.Headers["X-Accel-Buffering"] = "no";
        return Results.File(path, job.ContentType, job.FileName, enableRangeProcessing: true,
            lastModified: File.GetLastWriteTimeUtc(path), entityTag: new EntityTagHeaderValue('"' + job.Id + '"'));
    }

    /// <summary>只允许用户明确取消尚未生成完的下载。</summary>
    public void Cancel(PersistentDownload job) { if (job.State == "preparing") { job.Cancellation.Cancel(); } }

    /// <summary>所有下载入口共用的准备状态，按落盘字节报告进度，不暴露磁盘路径或伪造下载百分比。</summary>
    public object View(PersistentDownload job)
    {
        long preparedBytes = 0;
        try
        {
            var file = new FileInfo(PathOf(job) + (job.State == "preparing" ? ".part" : ""));
            if (file.Exists) { preparedBytes = file.Length; }
        }
        catch (IOException) { /* 原子发布或取消删除与状态查询并发时，下一轮重新读取。 */ }
        return new { job.Id, job.State, job.Error, job.FileName, preparedBytes };
    }

    /// <summary>在独立作用域和取消令牌下读取源资源并原子发布；断开的 HTTP 请求不会中止。</summary>
    private async Task GenerateAsync(PersistentDownload job)
    {
        var temporary = PathOf(job) + ".part";
        using var scope = _scopes.CreateScope();
        var services = scope.ServiceProvider;
        var cancellationToken = job.Cancellation.Token;
        try
        {
            await using (var target = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 128 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan))
            {
                var segments = job.Resource.Split('|');
                if (segments[0] == "project")
                {
                    var export = await services.GetRequiredService<ProjectExportService>().PrepareAsync(job.Owner, segments[1], segments[2], cancellationToken)
                        ?? throw new KeyNotFoundException("工程不存在或无权访问。");
                    job.FileName = export.FileName; job.ContentType = "application/zip";
                    long sourceBytes = 0;
                    foreach (var file in export.Files)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        if (file.OpenRead is null) { sourceBytes = checked(sourceBytes + file.Content.LongLength); }
                        else { using var source = file.OpenRead(); sourceBytes = checked(sourceBytes + source.Length); }
                    }
                    var volume = OperatingSystem.IsWindows() ? Path.GetPathRoot(_root)! : _root;
                    if (new DriveInfo(volume).AvailableFreeSpace < sourceBytes + sourceBytes / 10) { throw new IOException("下载缓存目录可用空间不足，需要容纳完整 ZIP 和安全余量。"); }
                    await ZipHelper.WriteAsync(export.Files, target, cancellationToken);
                }
                else
                {
                    var download = await OpenSourceAsync(services, job.Owner, segments, cancellationToken) ?? throw new FileNotFoundException("下载资源不存在或无权访问。");
                    job.FileName = download.FileName; job.ContentType = download.ContentType;
                    await using var source = download.Stream;
                    if (source.CanSeek)
                    {
                        var volume = OperatingSystem.IsWindows() ? Path.GetPathRoot(_root)! : _root;
                        if (new DriveInfo(volume).AvailableFreeSpace < source.Length + source.Length / 10) { throw new IOException("下载缓存目录可用空间不足。"); }
                    }
                    await source.CopyToAsync(target, 128 * 1024, cancellationToken);
                }
                await target.FlushAsync(cancellationToken);
            }
            cancellationToken.ThrowIfCancellationRequested();
            File.Move(temporary, PathOf(job)); job.State = "ready";
        }
        catch (OperationCanceledException) { job.State = "cancelled"; job.Error = "下载准备已取消。"; }
        catch (Exception error)
        {
            job.State = "failed"; job.Error = error.Message; job.StatusCode = error is KeyNotFoundException ? 404 : error is FileNotFoundException && job.Resource.StartsWith("project|") ? 400 : error is FileNotFoundException ? 404 : 400;
            _logger.LogWarning(error, "下载生成失败 {Id}", job.Id);
        }
        finally { if (File.Exists(temporary)) { File.Delete(temporary); } Save(job); }
    }

    /// <summary>沿用每个下载来源现有的所有权校验，所有临时流都由调用方释放。</summary>
    private static async Task<TrainingDownload?> OpenSourceAsync(IServiceProvider services, string owner, string[] key, CancellationToken cancellationToken)
    {
        if (key[0] == "model")
        {
            var result = await services.GetRequiredService<ManageOperate>().QueryAsync(owner, int.Parse(key[1]), cancellationToken);
            if (!result.GetDetails(out List<OnnxData>? models) || models?.FirstOrDefault(model => model.index == int.Parse(key[1])) is not { } model) { return null; }
            var path = Path.Combine(model.path ?? "", model.name ?? "");
            return File.Exists(path) ? new(OpenRead(path), model.name, "application/octet-stream") : null;
        }
        if (key[0] == "best")
        {
            var status = services.GetRequiredService<TrainingService>().GetStatus(owner, key[1]);
            return status?.BestModelPath is { } path && File.Exists(path) ? new(OpenRead(path), Path.GetFileName(path), "application/octet-stream") : null;
        }
        if (key[0] == "history")
        {
            return await services.GetRequiredService<TrainingHistoryService>().OpenDownloadAsync(key[1] == "yolo" ? ProjectKind.Yolo : ProjectKind.Anomalib, owner, key[2], key[3], key[4], cancellationToken);
        }
        var registry = services.GetRequiredService<AnomalibModelRegistry>();
        if (key[0] == "anomalib-model")
        {
            var model = await registry.FindAsync(owner, key[1], key[2], cancellationToken);
            return model is null ? null : new(await AnomalibModelRegistry.OpenPackageDownloadAsync(model, cancellationToken), key[2] + ".zip", "application/zip");
        }
        var stream = key[0] == "report" ? await registry.OpenTrainingReportDownloadAsync(owner, key[1], key[2], cancellationToken)
            : await registry.OpenTrainingPackageDownloadAsync(owner, key[1], key[2], cancellationToken);
        return stream is null ? null : new(stream, key[2] + (key[0] == "report" ? "-calibration.json" : ".zip"), key[0] == "report" ? "application/json" : "application/zip");
    }

    /// <summary>只读源文件，避免将下载生命周期与源文件删除绑定。</summary>
    private static FileStream OpenRead(string path) => new(path, FileMode.Open, FileAccess.Read, FileShare.Read, 128 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
    /// <summary>随机标识构造内部产物路径。</summary>
    private string PathOf(PersistentDownload job) => Path.Combine(_root, job.Id + ".bin");
    /// <summary>原子写入下载清单。</summary>
    private void Save(PersistentDownload job)
    {
        lock (_gate)
        {
            var path = Path.Combine(_root, job.Id + ".json");
            File.WriteAllText(path + ".tmp", JsonSerializer.Serialize(job)); File.Move(path + ".tmp", path, true);
        }
    }
    /// <summary>创建新任务时清理超过七天无人访问的终态缓存，绝不触碰训练源文件。</summary>
    private void CleanupExpired()
    {
        foreach (var job in _jobs.Values.Where(job => job.State != "preparing" && job.UpdatedAt < DateTime.UtcNow.AddDays(-7)))
        {
            try { File.Delete(PathOf(job)); File.Delete(Path.Combine(_root, job.Id + ".json")); _jobs.TryRemove(job.Id, out _); }
            catch (IOException error) { _logger.LogDebug(error, "下载文件仍在使用，稍后清理"); }
        }
    }
}
