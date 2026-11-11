namespace Snet.Yolo.Tasks.Services;

/// <summary>
/// 应用启动后在后台把已有上传图片缺失的预览图/缩略图补齐。
///
/// 升级到带缩略图的版本后，历史图片只有 1600 像素的预览（甚至一张都没有）；若等用户打开页面时才现场生成，
/// 每张都要先解码几十 MB 的原图。这里在启动 15 秒后用 1 个后台工作者按“最近上传优先”补齐，
/// 页面真正需要时通常已经就绪；补齐前页面请求仍然按需生成（与后台共用并发上限，互不拖垮）。
/// </summary>
public sealed class ImagePreviewWarmupService(ImagePreviewStore previews, ILogger<ImagePreviewWarmupService> logger) : BackgroundService
{
    private static readonly HashSet<string> ImageExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".jpg", ".jpeg", ".png", ".bmp", ".webp", ".gif",
    };

    /// <summary>用户上传文件的根目录。</summary>
    internal static string UploadsRoot => Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "wwwroot", "data", "uploads"));

    /// <summary>延迟后扫描上传目录并交给预览缓存补齐。</summary>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!previews.Enabled) { return; }
        try { await Task.Delay(TimeSpan.FromSeconds(15), stoppingToken).ConfigureAwait(false); }
        catch (OperationCanceledException) { return; }
        var root = UploadsRoot;
        if (!Directory.Exists(root)) { return; }
        try { previews.WarmUpMany(FindOriginals(root), 1, stoppingToken); }
        catch (Exception error) when (error is not OperationCanceledException)
        {
            logger.LogWarning(error, "启动时扫描上传图片以补齐预览失败，改为页面按需生成。");
        }
    }

    /// <summary>列出上传目录里的全部原图（跳过预览/缩略图及其临时文件），最近修改的排在前面。</summary>
    /// <param name="root">上传根目录。</param>
    internal static List<string> FindOriginals(string root)
        => Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
            .Where(path => ImageExtensions.Contains(Path.GetExtension(path)) && !ImagePreviewStore.IsDerived(Path.GetFileName(path)))
            .OrderByDescending(File.GetLastWriteTimeUtc)
            .ToList();
}
