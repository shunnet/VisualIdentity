namespace Snet.Yolo.Tasks.Services;

/// <summary>
/// 由已存储的图片地址（<c>/uploads/{owner}/{scope}/{fileName}</c>）生成缩略预览地址。
///
/// 列表/表格这类"看一眼"的位置都用预览（几十~几百 KB），
/// 只有标注页画布与查看器大图才加载原图（需要像素级精度）。
/// 预览由 <c>/images/preview</c> 端点在首次请求时生成并缓存，失败会自动回退原图。
/// </summary>
public static class ImagePreviewUrl
{
    private const string UploadsPrefix = "/uploads/";

    /// <summary>
    /// 主视图用的预览地址（最长边 ≤ 1600、≤ 400 KiB 的 JPEG）；无法解析（例如已是外部地址）时原样返回。
    /// </summary>
    /// <param name="url">存储地址。</param>
    public static string For(string? url) => Build(url, thumbnail: false);

    /// <summary>
    /// 列表/网格缩略图地址（最长边 ≤ 360、通常只有十几到三十 KB）。页面上缩略位只有几十到两百个 CSS 像素，
    /// 一页几十张图用它加载，在 1–2 MB/s 的现场网络上也能瞬间出齐；之后由浏览器缓存，返回页面不再请求。
    /// </summary>
    /// <param name="url">存储地址。</param>
    public static string ThumbFor(string? url) => Build(url, thumbnail: true);

    private static string Build(string? url, bool thumbnail)
    {
        if (string.IsNullOrWhiteSpace(url)) { return string.Empty; }
        var path = url.Split('?')[0];
        if (!path.StartsWith(UploadsPrefix, StringComparison.Ordinal)) { return url; }
        var segments = path[UploadsPrefix.Length..].Split('/');
        if (segments.Length < 3) { return url; }
        var scope = segments[^2];
        var fileName = segments[^1];
        if (scope.Length == 0 || fileName.Length == 0) { return url; }
        var address = "/images/preview?scope=" + Uri.EscapeDataString(scope) + "&name=" + Uri.EscapeDataString(fileName);
        return thumbnail ? address + "&size=thumb" : address;
    }
}