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

    /// <summary>生成预览地址；无法解析（例如已是外部地址）时原样返回。</summary>
    /// <param name="url">存储地址。</param>
    public static string For(string? url)
    {
        if (string.IsNullOrWhiteSpace(url)) { return string.Empty; }
        var path = url.Split('?')[0];
        if (!path.StartsWith(UploadsPrefix, StringComparison.Ordinal)) { return url; }
        var segments = path[UploadsPrefix.Length..].Split('/');
        if (segments.Length < 3) { return url; }
        var scope = segments[^2];
        var fileName = segments[^1];
        if (scope.Length == 0 || fileName.Length == 0) { return url; }
        return "/images/preview?scope=" + Uri.EscapeDataString(scope) + "&name=" + Uri.EscapeDataString(fileName);
    }
}