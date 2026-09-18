namespace Snet.Yolo.Tasks.Services;

/// <summary>
/// 动态导入前端脚本时的缓存参数：<c>./js/val-draw.js?v=&lt;文件时间戳&gt;</c>。
///
/// 背景：脚本是用 <c>IJSRuntime.import</c> 动态加载的，浏览器按"完整 URL（含查询串）"缓存。
/// 之前查询串是手写的固定日期（如 <c>?v=20260917</c>），改了脚本却忘了改它，浏览器就一直用旧脚本，
/// 表现为"代码明明改了、界面行为还是老的"。这里改成按脚本文件自身的最后写入时间生成，
/// 文件一改版本自动变化，杜绝这类问题。
/// </summary>
public static class WebAssetVersion
{

    /// <summary>给相对路径加上内容版本参数；文件不存在时用 0（等价于不缓存加速，行为安全）。</summary>
    /// <param name="relativePath">形如 <c>./js/val-draw.js</c> 或 <c>/js/app-shell.js</c>。</param>
    /// <param name="webRoot">静态资源根目录；默认取应用目录下的 wwwroot（测试可注入临时目录）。</param>
    public static string Versioned(string relativePath, string? webRoot = null)
    {
        if (string.IsNullOrWhiteSpace(relativePath)) { return relativePath; }
        if (relativePath.Contains("?v=", StringComparison.Ordinal)) { return relativePath; }
        var root = webRoot ?? Path.Combine(AppContext.BaseDirectory, "wwwroot");
        // 刻意不缓存：每次 import 只调用一两次，读一次文件时间戳的代价可以忽略，
        // 而缓存会让"重新发布后仍拿到旧版本号"，正是这个类要解决的问题。
        string version;
        try
        {
            var file = Path.Combine(root, relativePath.TrimStart('.', '/').Replace('/', Path.DirectorySeparatorChar));
            version = File.Exists(file)
                ? File.GetLastWriteTimeUtc(file).Ticks.ToString(System.Globalization.CultureInfo.InvariantCulture)
                : "0";
        }
        catch (IOException) { version = "0"; }
        catch (UnauthorizedAccessException) { version = "0"; }
        return relativePath + (relativePath.Contains('?') ? "&" : "?") + "v=" + version;
    }
}
