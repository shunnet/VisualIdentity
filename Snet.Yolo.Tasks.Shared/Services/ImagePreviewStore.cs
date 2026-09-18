namespace Snet.Yolo.Tasks.Services;

using System.Collections.Concurrent;

/// <summary>
/// 验证页预览图缓存：按需（或上传后台预热）为原图生成一张小预览，之后直接复用。
///
/// 设计要点：
///   · **原图永不改动** —— 预览写成同级文件 <c>&lt;原名&gt;.preview.jpg</c>，删除原图时一并删除；
///   · 生成一次即缓存，同一张图的并发请求由每文件的信号量合并（不会重复解码大图）；
///   · 生成失败只记日志并返回 null，调用方回退到原图，绝不影响上传与识别；
///   · 预览文件登记进 <see cref="ValidationFileLifetime"/>，随应用停止一起清理。
/// </summary>
public sealed class ImagePreviewStore
{
    private readonly ImagePreviewOptions _options;
    private readonly ValidationFileLifetime _lifetime;
    private readonly ILogger<ImagePreviewStore> _logger;
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _locks = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>构造预览缓存。</summary>
    public ImagePreviewStore(ImagePreviewOptions options, ValidationFileLifetime lifetime, ILogger<ImagePreviewStore> logger)
    {
        _options = options;
        _lifetime = lifetime;
        _logger = logger;
    }

    /// <summary>是否启用预览（关闭时调用方直接用原图）。</summary>
    public bool Enabled => _options.Enabled;

    /// <summary>某原图对应的预览文件路径（固定命名，便于删除与复用）。</summary>
    public static string PathFor(string originalPath) => originalPath + ".preview.jpg";

    /// <summary>已生成过就返回预览路径，否则返回 null（不触发生成）。</summary>
    public string? TryGetExisting(string originalPath)
    {
        if (!_options.Enabled) { return null; }
        var preview = PathFor(originalPath);
        return File.Exists(preview) ? preview : null;
    }

    /// <summary>
    /// 取得（必要时生成）预览图。返回 null 表示"用原图"：未启用、原图不存在或生成失败。
    /// </summary>
    /// <param name="originalPath">原图绝对路径。</param>
    /// <param name="cancellationToken">取消标记。</param>
    public async Task<string?> GetOrCreateAsync(string originalPath, CancellationToken cancellationToken = default)
    {
        if (!_options.Enabled) { return null; }
        var existing = TryGetExisting(originalPath);
        if (existing is not null) { return existing; }
        if (!File.Exists(originalPath)) { return null; }

        // 已够小且是 JPEG 的图直接当预览用（不再解码一遍）
        var info = new FileInfo(originalPath);
        if (!ImagePreviewGenerator.ShouldGenerate(originalPath, info.Length, 0, 0, _options)) { return originalPath; }

        var gate = _locks.GetOrAdd(originalPath, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            existing = TryGetExisting(originalPath);
            if (existing is not null) { return existing; }   // 等锁期间别人已经生成好了
            var previewPath = PathFor(originalPath);
            var generated = await Task.Run(() =>
            {
                var result = ImagePreviewGenerator.Generate(originalPath, _options);
                var temporary = previewPath + ".tmp";
                File.WriteAllBytes(temporary, result.Data);
                File.Move(temporary, previewPath, true);
                return result;
            }, cancellationToken).ConfigureAwait(false);
            _lifetime.Track(previewPath);
            _logger.LogInformation("验证页预览已生成 {Name}：{Note}", Path.GetFileName(originalPath), generated.Note);
            return previewPath;
        }
        catch (Exception error) when (error is not OperationCanceledException)
        {
            _logger.LogWarning(error, "验证页预览生成失败，改用原图：{Name}", Path.GetFileName(originalPath));
            return null;
        }
        finally
        {
            gate.Release();
        }
    }

    /// <summary>
    /// 批量后台预热：项目/分类图片导入后调用，让用户真正查看时预览已经就绪（否则每张首看都要现场解码几十 MB）。
    /// 并发受限（默认 2）避免同时解码多张大图造成内存尖峰；已缓存、不存在的直接跳过；失败只记日志。
    /// </summary>
    /// <param name="paths">原图绝对路径集合。</param>
    /// <param name="concurrency">并发生成数。</param>
    public void WarmUpMany(IEnumerable<string> paths, int concurrency = 2)
    {
        if (!_options.Enabled) { return; }
        var pending = paths.Where(File.Exists).Where(path => !File.Exists(PathFor(path))).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (pending.Count == 0) { return; }
        _logger.LogInformation("开始预热 {Count} 张图片的预览（并发 {Concurrency}）", pending.Count, concurrency);
        _ = Task.Run(async () =>
        {
            using var gate = new SemaphoreSlim(Math.Clamp(concurrency, 1, 4));
            var tasks = pending.Select(async path =>
            {
                await gate.WaitAsync().ConfigureAwait(false);
                try { await GetOrCreateAsync(path).ConfigureAwait(false); }
                catch (Exception error) { _logger.LogDebug(error, "预览预热失败：{Name}", Path.GetFileName(path)); }
                finally { gate.Release(); }
            });
            await Task.WhenAll(tasks).ConfigureAwait(false);
            _logger.LogInformation("预览预热完成：{Count} 张", pending.Count);
        });
    }
    /// <summary>上传完成后台预热预览：不阻塞上传，失败只记日志。</summary>
    public void WarmUp(string originalPath)
    {
        if (!_options.Enabled) { return; }
        _ = Task.Run(async () =>
        {
            try { await GetOrCreateAsync(originalPath).ConfigureAwait(false); }
            catch (Exception error) { _logger.LogDebug(error, "验证页预览预热失败：{Name}", Path.GetFileName(originalPath)); }
        });
    }

    /// <summary>删除原图时一并删除它的预览。</summary>
    public void Delete(string originalPath)
    {
        var preview = PathFor(originalPath);
        try
        {
            if (File.Exists(preview))
            {
                File.Delete(preview);
                _lifetime.Untrack(preview);
            }
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
