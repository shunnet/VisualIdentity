namespace Snet.Yolo.Tasks.Services;

using System.Collections.Concurrent;

/// <summary>图片派生图的种类。</summary>
public enum ImageVariant
{
    /// <summary>主视图用的预览图：最长边 ≤ 1600、≤ 400 KiB。</summary>
    Preview,
    /// <summary>列表/网格用的缩略图：最长边 ≤ 360、通常十几到三十 KB。</summary>
    Thumbnail,
}

/// <summary>
/// 图片派生图缓存：按需（或上传后台预热）为原图生成「预览图」与「缩略图」，之后直接复用。
///
/// 设计要点：
///   · **原图永不改动** —— 派生图写成同级文件 <c>&lt;原名&gt;.preview.jpg</c> / <c>&lt;原名&gt;.thumb.jpg</c>，删除原图时一并删除；
///   · 生成一次即缓存，同一张图的并发请求由每文件的信号量合并（不会重复解码大图）；
///   · 全局并发上限（默认 2）：一次最多解码 2 张大图，既避免内存尖峰，也让先到的请求先出图而不是所有图一起慢；
///   · 大图只解码一次，预览图与缩略图同时产出；已有预览时缩略图直接从预览缩（解码 ≤1600 像素的小图，快几十倍）；
///   · 生成失败只记日志并返回 null，调用方回退到原图，绝不影响上传与识别；
///   · **只有验证临时文件的派生图**才登记进 <see cref="ValidationFileLifetime"/>（随应用停止一起清理）；
///     工程图片是持久数据，它们的派生图也必须持久 —— 否则每次重启后所有缩略图都要重新解码几十 MB 的原图，页面就会整体变慢。
/// </summary>
public sealed class ImagePreviewStore
{
    private readonly ImagePreviewOptions _options;
    private readonly ValidationFileLifetime _lifetime;
    private readonly ILogger<ImagePreviewStore> _logger;
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _locks = new(StringComparer.OrdinalIgnoreCase);
    private readonly SemaphoreSlim _generation;

    /// <summary>构造预览缓存。</summary>
    public ImagePreviewStore(ImagePreviewOptions options, ValidationFileLifetime lifetime, ILogger<ImagePreviewStore> logger)
    {
        _options = options;
        _lifetime = lifetime;
        _logger = logger;
        _generation = new SemaphoreSlim(Math.Clamp(options.MaxConcurrentGenerations, 1, 8));
    }

    /// <summary>是否启用预览（关闭时调用方直接用原图）。</summary>
    public bool Enabled => _options.Enabled;

    /// <summary>某原图对应的预览文件路径（固定命名，便于删除与复用）。</summary>
    public static string PathFor(string originalPath) => originalPath + ".preview.jpg";

    /// <summary>某原图对应的缩略图文件路径。</summary>
    public static string ThumbPathFor(string originalPath) => originalPath + ".thumb.jpg";

    /// <summary>某原图指定种类的派生图路径。</summary>
    public static string PathFor(string originalPath, ImageVariant variant)
        => variant == ImageVariant.Thumbnail ? ThumbPathFor(originalPath) : PathFor(originalPath);

    /// <summary>某原图的全部派生图路径（删除原图时一并删除）。</summary>
    public static IEnumerable<string> DerivedPaths(string originalPath)
    {
        yield return PathFor(originalPath);
        yield return ThumbPathFor(originalPath);
    }

    /// <summary>该文件名是不是派生图（预览/缩略图及其临时文件），扫描原图时据此跳过。</summary>
    public static bool IsDerived(string fileName)
        => fileName.EndsWith(".preview.jpg", StringComparison.OrdinalIgnoreCase)
        || fileName.EndsWith(".thumb.jpg", StringComparison.OrdinalIgnoreCase)
        || fileName.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase);

    /// <summary>已生成过预览就返回预览路径，否则返回 null（不触发生成）。</summary>
    public string? TryGetExisting(string originalPath) => TryGetExisting(originalPath, ImageVariant.Preview);

    /// <summary>已生成过指定种类的派生图就返回其路径，否则返回 null（不触发生成）。</summary>
    public string? TryGetExisting(string originalPath, ImageVariant variant)
    {
        if (!_options.Enabled) { return null; }
        var path = PathFor(originalPath, variant);
        return File.Exists(path) ? path : null;
    }

    /// <summary>取得（必要时生成）预览图。返回 null 表示"用原图"：未启用、原图不存在或生成失败。</summary>
    /// <param name="originalPath">原图绝对路径。</param>
    /// <param name="cancellationToken">取消标记。</param>
    public Task<string?> GetOrCreateAsync(string originalPath, CancellationToken cancellationToken = default)
        => GetOrCreateAsync(originalPath, ImageVariant.Preview, cancellationToken);

    /// <summary>
    /// 取得（必要时生成）指定种类的派生图。返回原图路径表示原图已经足够小可直接使用；返回 null 表示"用原图"：
    /// 未启用、原图不存在或生成失败。
    /// </summary>
    /// <param name="originalPath">原图绝对路径。</param>
    /// <param name="variant">预览图或缩略图。</param>
    /// <param name="cancellationToken">取消标记。</param>
    public async Task<string?> GetOrCreateAsync(string originalPath, ImageVariant variant, CancellationToken cancellationToken = default)
    {
        if (!_options.Enabled) { return null; }
        var existing = TryGetExisting(originalPath, variant);
        if (existing is not null) { return existing; }
        if (!File.Exists(originalPath)) { return null; }

        // 已够小且是 JPEG 的图直接当派生图用（不再解码一遍）
        var length = new FileInfo(originalPath).Length;
        if (!NeedsDerived(originalPath, length, variant)) { return originalPath; }

        var gate = _locks.GetOrAdd(originalPath, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            existing = TryGetExisting(originalPath, variant);
            if (existing is not null) { return existing; }   // 等锁期间别人已经生成好了
            await _generation.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                var note = await Task.Run(() => CreateFiles(originalPath, length), cancellationToken).ConfigureAwait(false);
                _logger.LogInformation("图片派生图已生成 {Name}：{Note}", Path.GetFileName(originalPath), note);
            }
            finally
            {
                _generation.Release();
            }
            return PathFor(originalPath, variant);
        }
        catch (Exception error) when (error is not OperationCanceledException)
        {
            _logger.LogWarning(error, "图片派生图生成失败，改用原图：{Name}", Path.GetFileName(originalPath));
            return null;
        }
        finally
        {
            gate.Release();
        }
    }

    /// <summary>该种类的派生图对这张原图是否有必要（原图已经够小的 JPEG 就直接用原图）。</summary>
    private bool NeedsDerived(string originalPath, long length, ImageVariant variant)
        => variant == ImageVariant.Thumbnail
            ? !ImagePreviewGenerator.IsThumbnailSized(originalPath, length, _options)
            : ImagePreviewGenerator.ShouldGenerate(originalPath, length, 0, 0, _options);

    /// <summary>
    /// 生成缺失的派生图并原子写入。两份都缺时只解码一次原图；只缺缩略图而预览已在时，直接从预览缩。
    /// </summary>
    /// <returns>写入摘要，用于日志。</returns>
    private string CreateFiles(string originalPath, long length)
    {
        var previewPath = PathFor(originalPath);
        var thumbPath = ThumbPathFor(originalPath);
        var previewNeeded = !File.Exists(previewPath) && NeedsDerived(originalPath, length, ImageVariant.Preview);
        var thumbNeeded = !File.Exists(thumbPath) && NeedsDerived(originalPath, length, ImageVariant.Thumbnail);
        if (previewNeeded && thumbNeeded)
        {
            var (preview, thumbnail) = ImagePreviewGenerator.GenerateBoth(originalPath, _options);
            Write(originalPath, previewPath, preview);
            Write(originalPath, thumbPath, thumbnail);
            return $"预览 {preview.Note}；缩略图 {thumbnail.Bytes / 1024d:0.#} KB";
        }
        if (previewNeeded)
        {
            var preview = ImagePreviewGenerator.Generate(originalPath, _options);
            Write(originalPath, previewPath, preview);
            return "预览 " + preview.Note;
        }
        if (thumbNeeded)
        {
            // 预览已经存在就从预览缩（≤1600 像素的小 JPEG，几毫秒）；原图本身够小、没有预览时才读原图。
            var source = File.Exists(previewPath) ? previewPath : originalPath;
            var thumbnail = ImagePreviewGenerator.GenerateThumbnail(source, _options);
            Write(originalPath, thumbPath, thumbnail);
            return $"缩略图 {thumbnail.Bytes / 1024d:0.#} KB";
        }
        return "无需生成";
    }

    private void Write(string originalPath, string destination, GeneratedImagePreview result)
    {
        var temporary = destination + ".tmp";
        File.WriteAllBytes(temporary, result.Data);
        File.Move(temporary, destination, true);
        // 只有验证临时文件的派生图才随应用停止清理；工程图片的派生图是持久缓存。
        if (_lifetime.IsTracked(originalPath)) { _lifetime.Track(destination); }
    }

    /// <summary>派生图（预览与缩略图）是否都已就绪或无需生成。</summary>
    public bool IsComplete(string originalPath)
    {
        if (!_options.Enabled) { return true; }
        var info = new FileInfo(originalPath);
        if (!info.Exists) { return true; }
        var previewReady = File.Exists(PathFor(originalPath)) || !NeedsDerived(originalPath, info.Length, ImageVariant.Preview);
        var thumbReady = File.Exists(ThumbPathFor(originalPath)) || !NeedsDerived(originalPath, info.Length, ImageVariant.Thumbnail);
        return previewReady && thumbReady;
    }

    /// <summary>
    /// 批量后台预热：项目/分类图片导入后调用，让用户真正查看时派生图已经就绪（否则每张首看都要现场解码几十 MB）。
    /// 默认只用 1 个后台工作者，另一个生成名额留给页面正在等的请求；已缓存、不存在的直接跳过；失败只记日志。
    /// </summary>
    /// <param name="paths">原图绝对路径集合。</param>
    /// <param name="concurrency">后台并发生成数。</param>
    /// <param name="cancellationToken">取消标记（应用停止时终止预热）。</param>
    public void WarmUpMany(IEnumerable<string> paths, int concurrency = 1, CancellationToken cancellationToken = default)
    {
        if (!_options.Enabled) { return; }
        var pending = paths.Where(File.Exists).Distinct(StringComparer.OrdinalIgnoreCase).Where(path => !IsComplete(path)).ToList();
        if (pending.Count == 0) { return; }
        _logger.LogInformation("开始预热 {Count} 张图片的预览与缩略图（并发 {Concurrency}）", pending.Count, concurrency);
        _ = Task.Run(async () =>
        {
            using var gate = new SemaphoreSlim(Math.Clamp(concurrency, 1, 4));
            var tasks = pending.Select(async path =>
            {
                try { await gate.WaitAsync(cancellationToken).ConfigureAwait(false); }
                catch (OperationCanceledException) { return; }
                try
                {
                    // 预览先、缩略图后：两份都缺时第一次调用就一并产出，第二次直接命中。
                    await GetOrCreateAsync(path, ImageVariant.Preview, cancellationToken).ConfigureAwait(false);
                    await GetOrCreateAsync(path, ImageVariant.Thumbnail, cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException) { }
                catch (Exception error) { _logger.LogDebug(error, "图片派生图预热失败：{Name}", Path.GetFileName(path)); }
                finally { gate.Release(); }
            });
            await Task.WhenAll(tasks).ConfigureAwait(false);
            _logger.LogInformation("图片派生图预热完成：{Count} 张", pending.Count);
        });
    }

    /// <summary>上传完成后台预热派生图：不阻塞上传，失败只记日志。</summary>
    public void WarmUp(string originalPath) => WarmUpMany([originalPath]);

    /// <summary>删除原图时一并删除它的预览与缩略图。</summary>
    public void Delete(string originalPath)
    {
        foreach (var derived in DerivedPaths(originalPath))
        {
            try
            {
                if (File.Exists(derived))
                {
                    File.Delete(derived);
                    _lifetime.Untrack(derived);
                }
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }
}
