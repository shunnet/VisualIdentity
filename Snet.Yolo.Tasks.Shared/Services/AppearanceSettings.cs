namespace Snet.Yolo.Tasks.Services;

/// <summary>站点品牌及新浏览器默认外观；不可变快照避免跨电路修改共享状态。</summary>
public sealed record AppearanceSettings
{
    /// <summary>侧栏、登录页及浏览器标题中的品牌名称。</summary>
    public string Title { get; init; } = "Snet.Yolo";
    /// <summary>品牌说明；空值使用各位置原有说明。</summary>
    public string Description { get; init; } = "";
    /// <summary>经解码重新编码的 PNG 图片；空值使用内置图片。</summary>
    public string? LogoDataUrl { get; init; }
    /// <summary>新浏览器默认语言。</summary>
    public string Language { get; init; } = "zh-CN";
    /// <summary>新浏览器默认主题；空值跟随操作系统。</summary>
    public string? Theme { get; init; }
    /// <summary>用于图片元素的可信地址。</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public string LogoUrl => LogoDataUrl ?? "/favicon.png";
}
