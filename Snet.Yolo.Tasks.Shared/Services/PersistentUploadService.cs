using System.Collections.Concurrent;
using System.Security.Claims;
using System.Text.Json;

namespace Snet.Yolo.Tasks.Services;

/// <summary>管理按用户隔离的分块上传及独立导入；刷新和请求断开不取消已接收的任务。</summary>
public sealed class PersistentUploadService
{
    /// <summary>单个请求的最大分块，128 GiB 文件也只使用固定大小缓冲区。</summary>
    public const int ChunkBytes = 4 * 1024 * 1024;
    private readonly ConcurrentDictionary<string, PersistentUpload> _jobs = new();
    private readonly IServiceScopeFactory _scopes;
    private readonly ILogger<PersistentUploadService> _logger;
    private readonly string _root;
    private readonly object _creationGate = new();

    /// <summary>加载已有偏移记录；进程中断的导入标记失败，避免重复追加业务数据。</summary>
    public PersistentUploadService(IServiceScopeFactory scopes, ILogger<PersistentUploadService> logger)
    {
        _scopes = scopes; _logger = logger;
        _root = Path.Combine(AppContext.BaseDirectory, "transfers", "uploads");
        Directory.CreateDirectory(_root);
        foreach (var path in Directory.EnumerateFiles(_root, "manifest.json", SearchOption.AllDirectories))
        {
            try
            {
                var job = JsonSerializer.Deserialize<PersistentUpload>(File.ReadAllText(path));
                if (job is null || !Guid.TryParseExact(job.Id, "N", out _) || Path.GetDirectoryName(path) != DirectoryOf(job)) { continue; }
                if (job.State == "processing") { job.State = "failed"; job.Error = "服务重启中断了导入，请检查工程后重新提交。"; job.LastProgress = null; Save(job); DeleteParts(job); }
                _jobs.TryAdd(job.Id, job);
            }
            catch (Exception error) when (error is IOException or JsonException or ArgumentException)
            { logger.LogWarning(error, "无法恢复上传记录 {Path}", path); }
        }
    }

    /// <summary>任意用户的任务变化；订阅端必须按用户名过滤。</summary>
    public event Action<string>? Changed;

    /// <summary>仅返回认证用户自己的记录。</summary>
    public PersistentUpload? Find(string owner, string id)
        => _jobs.TryGetValue(id, out var job) && job.Owner == owner ? job : null;

    /// <summary>读取当前用户的任务列表。</summary>
    public IReadOnlyList<PersistentUpload> List(string owner) => _jobs.Values.Where(job => job.Owner == owner).OrderBy(job => job.UpdatedAt).ToArray();

    /// <summary>转换为页面已有的统一进度快照。</summary>
    public UploadJob Snapshot(PersistentUpload job)
    {
        if (job.Progress is { } progress)
        {
            // 业务导入结束后仍需清理暂存文件；这段时间不能解除上传门禁或显示已完成。
            return job.State == "processing" && !progress.IsRunning
                ? progress with { ScopeKey = job.Intent.ValidationScopeKey, Phase = UploadPhase.Running, Status = "正在完成上传并清理暂存文件…" }
                : progress with { ScopeKey = job.Intent.ValidationScopeKey };
        }
        var total = job.Files.Sum(file => file.Size);
        var received = job.Offsets.Sum();
        var phase = job.State switch { "completed" => UploadPhase.Completed, "failed" => UploadPhase.Failed, "cancelled" => UploadPhase.Cancelled, _ => UploadPhase.Running };
        var bytes = $"{UploadCenter.FormatBytes(received)} / {UploadCenter.FormatBytes(total)}";
        var status = job.State == "uploading" ? job.SourcePhase switch
        {
            "preparing" => "准备续传来源，尚未开始网络上传（" + bytes + "）",
            "awaiting-source" => "请选择原文件以续传，已上传 " + bytes,
            "paused" => "上传已暂停，已上传 " + bytes,
            _ => "上传文件 " + bytes,
        } : "准备导入…";
        return new UploadJob(job.Intent.ValidationScopeKey, job.Intent.Kind, phase, job.Error ?? status,
            job.Files.Length, job.Offsets.Where((offset, index) => offset == job.Files[index].Size).Count(),
            total == 0 ? 0 : (int)(received * 100d / total), job.Error, job.UpdatedAt) { TransferStage = job.State == "uploading" ? job.SourcePhase : null };
    }

    /// <summary>记录客户端的真实来源阶段，避免本地准备被显示为上传 0%。</summary>
    public async Task SetSourcePhaseAsync(PersistentUpload job, string phase, CancellationToken cancellationToken)
    {
        if (phase is not ("preparing" or "uploading" or "awaiting-source" or "paused")) { throw new InvalidDataException("上传阶段无效。"); }
        await job.Gate.WaitAsync(cancellationToken);
        try { if (job.State == "uploading") { job.SourcePhase = phase; Save(job); } }
        finally { job.Gate.Release(); Notify(job.Owner); }
    }

    /// <summary>重选原文件后核对文件开头和断点前各最多 64 KiB 的内容，避免混入另一份同名文件。</summary>
    public async Task VerifySourceAsync(PersistentUpload job, int index, Stream proof, CancellationToken cancellationToken)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, job.Cancellation.Token);
        cancellationToken = linked.Token;
        await job.Gate.WaitAsync(cancellationToken);
        try
        {
            if (job.State != "uploading" || index < 0 || index >= job.Files.Length) { throw new InvalidOperationException("上传任务不可恢复。"); }
            var length = (int)Math.Min(64 * 1024, job.Offsets[index]);
            var supplied = new byte[length * 2];
            await proof.ReadExactlyAsync(supplied, cancellationToken);
            var extra = new byte[1];
            if (await proof.ReadAsync(extra, cancellationToken) != 0) { throw new InvalidDataException("文件恢复校验长度无效。"); }
            if (length == 0) { return; }
            var expected = new byte[length * 2];
            await using var source = new FileStream(FileOf(job, index), FileMode.Open, FileAccess.Read, FileShare.Read, 64 * 1024, FileOptions.Asynchronous);
            await source.ReadExactlyAsync(expected.AsMemory(0, length), cancellationToken);
            source.Position = job.Offsets[index] - length;
            await source.ReadExactlyAsync(expected.AsMemory(length, length), cancellationToken);
            if (!System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(supplied, expected))
            { throw new InvalidDataException("所选文件内容与已上传分块不一致，请选择原文件。"); }
        }
        finally { job.Gate.Release(); }
    }

    /// <summary>创建任务前验证真实用户、目标所有权、类型与文件限制，拒绝并发重复提交。</summary>
    public async Task<PersistentUpload> CreateAsync(ClaimsPrincipal principal, UploadIntent intent, UploadDraft draft, TransferFile[] files)
    {
        var owner = principal.Identity?.Name;
        if (principal.Identity?.IsAuthenticated != true || string.IsNullOrWhiteSpace(owner)) { throw new UnauthorizedAccessException(); }
        if (files.Length is < 1 or > UploadCenter.MaxFilesPerSelection || string.IsNullOrWhiteSpace(intent.ScopeKey)) { throw new InvalidDataException("上传任务无效。"); }
        if (!Enum.IsDefined(intent.Kind) || ((intent.Kind is UploadKind.ProjectYoloArchive or UploadKind.ValidationModel or UploadKind.AnomalibModelPackage or UploadKind.BrandLogo) && files.Length != 1))
        { throw new InvalidDataException("上传文件数量或类型无效。"); }
        foreach (var file in files)
        {
            if (file.Name.Length > 255 || file.Name != Path.GetFileName(file.Name) || !UploadCenter.IsAcceptable(intent.Kind, file.Name, file.Size, out var reason))
            { throw new InvalidDataException("文件格式或大小无效：" + file.Name); }
        }
        using var scope = _scopes.CreateScope();
        scope.ServiceProvider.GetRequiredService<CurrentUserContext>().SetTransferPrincipal(principal);
        if (intent.Kind is UploadKind.ProjectImages or UploadKind.ProjectClassImages or UploadKind.ProjectYoloArchive or UploadKind.AnomalibProjectImages)
        {
            var expected = intent.Kind == UploadKind.AnomalibProjectImages ? Server.models.ProjectKind.Anomalib : Server.models.ProjectKind.Yolo;
            if (await scope.ServiceProvider.GetRequiredService<WorkspaceService>().GetProjectAsync(intent.ProjectId, expected) is null) { throw new UnauthorizedAccessException("工程不存在或无权访问。"); }
        }
        if (intent.Kind == UploadKind.BrandLogo && !principal.IsInRole("Admin")) { throw new UnauthorizedAccessException(); }
        if (intent.Kind == UploadKind.ValidationImages && !(await scope.ServiceProvider.GetRequiredService<ValidationService>().GetModelsAsync()).Any(model => model.index == intent.ModelIndex))
        { throw new UnauthorizedAccessException("模型不存在或无权访问。"); }
        if (intent.Kind == UploadKind.AnomalibValidationImages && await scope.ServiceProvider.GetRequiredService<Server.anomalib.AnomalibModelRegistry>().FindAsync(owner, intent.ProjectId, intent.RunId) is null)
        { throw new UnauthorizedAccessException("模型不存在或无权访问。"); }
        lock (_creationGate)
        {
            if (List(owner).Any(job => job.State is "uploading" or "processing")) { throw new InvalidOperationException("已有上传任务，请先完成或取消。"); }
            var bytes = files.Sum(file => file.Size);
            var volume = OperatingSystem.IsWindows() ? Path.GetPathRoot(_root)! : _root;
            if (new DriveInfo(volume).AvailableFreeSpace < bytes + bytes / 10) { throw new IOException("上传暂存目录空间不足，需要容纳完整文件和安全余量。"); }
            var job = new PersistentUpload { Owner = owner, Intent = intent, Draft = draft, Files = files, Offsets = new long[files.Length] };
            Directory.CreateDirectory(DirectoryOf(job));
            Save(job); _jobs[job.Id] = job; Notify(owner); return job;
        }
    }

    /// <summary>关闭终态提示并持久删除记录，不影响业务文件。</summary>
    public void Clear(string owner, string scopeKey)
    {
        foreach (var job in List(owner).Where(job => job.Intent.ValidationScopeKey == scopeKey && job.State is not ("uploading" or "processing")))
        {
            File.Delete(Path.Combine(DirectoryOf(job), "manifest.json"));
            _jobs.TryRemove(job.Id, out _);
        }
        Notify(owner);
    }

    /// <summary>严格按偏移追加有界分块；重复或乱序请求由客户端重新查询权威偏移恢复。</summary>
    public async Task AppendAsync(PersistentUpload job, int index, long offset, Stream source, CancellationToken cancellationToken)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, job.Cancellation.Token);
        cancellationToken = linked.Token;
        await job.Gate.WaitAsync(cancellationToken);
        try
        {
            if (job.State != "uploading" || index < 0 || index >= job.Files.Length || offset != job.Offsets[index]) { throw new InvalidOperationException("上传偏移已变化，请重新查询。"); }
            var path = FileOf(job, index);
            var maximum = Math.Min(ChunkBytes, job.Files[index].Size - offset);
            await using var target = new FileStream(path, FileMode.OpenOrCreate, FileAccess.Write, FileShare.None, 64 * 1024, FileOptions.Asynchronous);
            target.SetLength(offset); target.Position = offset;
            var buffer = new byte[64 * 1024]; long copied = 0;
            try
            {
                int read;
                while ((read = await source.ReadAsync(buffer, cancellationToken)) > 0)
                {
                    copied += read;
                    if (copied > maximum) { throw new InvalidDataException("上传分块超过允许长度。"); }
                    await target.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                }
                if (copied == 0) { throw new InvalidDataException("上传分块为空。"); }
                await target.FlushAsync(cancellationToken);
                job.Offsets[index] = offset + copied;
                job.SourcePhase = "uploading";
                try { Save(job); } catch { job.Offsets[index] = offset; throw; }
            }
            catch { target.SetLength(offset); throw; }
        }
        finally { job.Gate.Release(); Notify(job.Owner); }
    }

    /// <summary>全部分块接收后只启动一次导入；任务持有独立 DI 作用域。</summary>
    public async Task CompleteAsync(PersistentUpload job, ClaimsPrincipal principal)
    {
        if (principal.Identity?.IsAuthenticated != true || principal.Identity.Name != job.Owner) { throw new UnauthorizedAccessException(); }
        await job.Gate.WaitAsync();
        try
        {
            if (job.State is "processing" or "completed") { return; }
            if (job.State != "uploading" || job.Offsets.Where((offset, index) => offset != job.Files[index].Size).Any()) { throw new InvalidOperationException("文件尚未上传完整。"); }
            job.State = "processing"; Save(job);
            _ = Task.Run(() => ProcessAsync(job, principal));
        }
        finally { job.Gate.Release(); Notify(job.Owner); }
    }

    /// <summary>取消分块或后台导入；已接收的临时文件在流关闭后删除。</summary>
    public async Task CancelAsync(PersistentUpload job)
    {
        if (job.State is not ("uploading" or "processing")) { return; }
        // 先中断正在接收的慢分块，再等待其关闭文件；不能拿着同一把锁等待用户取消。
        job.Cancellation.Cancel();
        job.Processor?.Cancel(job.Intent.ScopeKey);
        await job.Gate.WaitAsync();
        try
        {
            if (job.State is not ("uploading" or "processing")) { return; }
            if (job.State == "uploading") { DeleteParts(job); job.State = "cancelled"; Save(job); }
        }
        finally { job.Gate.Release(); Notify(job.Owner); }
    }

    /// <summary>执行现有内容验证和业务写入，不持有 HttpContext 或页面。</summary>
    private async Task ProcessAsync(PersistentUpload job, ClaimsPrincipal principal)
    {
        using var scope = _scopes.CreateScope();
        var terminalState = "failed";
        string? terminalError = null;
        try
        {
            scope.ServiceProvider.GetRequiredService<CurrentUserContext>().SetTransferPrincipal(principal);
            var processor = scope.ServiceProvider.GetRequiredService<UploadCenter>();
            job.Processor = processor;
            processor.Changed += () => Notify(job.Owner);
            await processor.RunStagedAsync(job.Intent, job.Draft, job.Files.Select((file, index) => new StagedBrowserFile(FileOf(job, index), file)).ToArray(), job.Cancellation.Token);
            var result = processor.GetJob(job.Intent.ScopeKey);
            terminalState = result?.Phase switch { UploadPhase.Completed => "completed", UploadPhase.Cancelled => "cancelled", _ => "failed" };
            terminalError = result?.Error;
        }
        catch (Exception error)
        {
            terminalState = job.Cancellation.IsCancellationRequested ? "cancelled" : "failed"; terminalError = error.Message;
            _logger.LogError(error, "持久上传导入失败 {Id}", job.Id);
        }
        finally
        {
            // 先完成清理，再公开终态，避免调用方看到完成后仍读到 .part 或提前启动下一批。
            DeleteParts(job);
            if (job.Processor?.GetJob(job.Intent.ScopeKey) is { IsRunning: false } progress) { job.LastProgress = progress; }
            job.Processor = null;
            job.Error = terminalError;
            job.State = terminalState;
            try { Save(job); }
            catch (IOException error) { _logger.LogError(error, "无法保存上传终态 {Id}", job.Id); }
            finally { Notify(job.Owner); }
        }
    }

    /// <summary>内部路径只由随机任务标识和文件序号构造。</summary>
    private string DirectoryOf(PersistentUpload job) => Path.Combine(_root, job.Id);
    /// <summary>业务文件名不参与暂存路径。</summary>
    private string FileOf(PersistentUpload job, int index) => Path.Combine(DirectoryOf(job), index + ".part");
    /// <summary>原子替换偏移清单，重试以清单而非残余文件长度为准。</summary>
    private void Save(PersistentUpload job)
    {
        job.UpdatedAt = DateTime.UtcNow;
        var path = Path.Combine(DirectoryOf(job), "manifest.json");
        File.WriteAllText(path + ".tmp", JsonSerializer.Serialize(job)); File.Move(path + ".tmp", path, true);
    }
    /// <summary>仅删除本任务的分块；保留终态记录用于刷新后解释结果。</summary>
    private void DeleteParts(PersistentUpload job)
    {
        for (var index = 0; index < job.Files.Length; index++)
        {
            try { File.Delete(FileOf(job, index)); }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException) { _logger.LogWarning(error, "上传暂存文件清理失败 {Id}", job.Id); }
        }
    }
    /// <summary>广播进度，不允许失效页面订阅影响数据提交。</summary>
    private void Notify(string owner)
    {
        foreach (var callback in Changed?.GetInvocationList() ?? [])
        { try { ((Action<string>)callback)(owner); } catch (Exception error) { _logger.LogDebug(error, "上传订阅已失效"); } }
    }
}
