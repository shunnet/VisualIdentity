namespace Snet.Yolo.Tasks.Services;

/// <summary>
/// 列表页面状态的 URL 构造：把页码、搜索词、展开项等放进查询参数，
/// 这样**刷新浏览器、复制链接、前进后退**都能回到同一处，而不是跳回初始状态。
///
/// 约定：值为空 / 页码为 1 的条目会被省略，保持链接干净。
/// </summary>
public static class ListStateUrl
{
    /// <summary>构造带状态查询参数的地址（绝对地址，供 NavigationManager.NavigateTo 使用）。</summary>
    /// <param name="baseUri">应用基地址（NavigationManager.BaseUri）。</param>
    /// <param name="path">页面路径，例如 <c>project/abc</c>（不含前导斜杠）。</param>
    /// <param name="state">状态键值；空值会被跳过。</param>
    public static string Build(string? baseUri, string path, params (string Key, string? Value)[] state)
    {
        var root = string.IsNullOrWhiteSpace(baseUri) ? "/" : baseUri;
        var url = root.TrimEnd('/') + "/" + (path ?? string.Empty).TrimStart('/');
        var query = new List<string>();
        foreach (var (key, value) in state)
        {
            if (string.IsNullOrWhiteSpace(key) || string.IsNullOrWhiteSpace(value)) { continue; }
            query.Add(Uri.EscapeDataString(key) + "=" + Uri.EscapeDataString(value.Trim()));
        }
        return query.Count == 0 ? url : url + "?" + string.Join('&', query);
    }

    /// <summary>
    /// 带分页的列表页常用形态：页码 + 搜索词 + 展开项（页码 ≤ 1 与空值自动省略）。
    /// 用它而不是手写元组 —— 手写时极易把"页码"当成键写进 (Key, Value)，结果空值被跳过、页码丢失。
    /// </summary>
    /// <param name="baseUri">应用基地址。</param>
    /// <param name="path">页面路径，如 <c>project/abc</c>。</param>
    /// <param name="page">页码。</param>
    /// <param name="search">搜索词。</param>
    /// <param name="expanded">展开项（如分类文件夹名）。</param>
    /// <param name="expandedKey">展开项的查询参数名。</param>
    public static string BuildList(string? baseUri, string path, int page, string? search = null, string? expanded = null, string? expandedKey = "folder")
        => Build(baseUri, path, ("page", Page(page)), ("q", search), (expandedKey ?? "folder", expanded));
    /// <summary>页码便捷写法：第 1 页省略（返回 null 表示不放进查询串）。</summary>
    /// <param name="page">页码。</param>
    public static string? Page(int page) => page > 1 ? page.ToString(System.Globalization.CultureInfo.InvariantCulture) : null;
}
