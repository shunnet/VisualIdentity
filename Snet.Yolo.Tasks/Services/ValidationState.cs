namespace Snet.Yolo.Tasks.Services;

/// <summary>识别会话状态（Scoped，跨页面/模型切换保留，关闭程序才清空）。</summary>
public sealed class ValidationState
{
    /// <summary>累加的识别检测（标签/置信度/坐标）。</summary>
    public List<(string Name, string Conf, string Pos)> Detections { get; } = new();

    /// <summary>最近一次识别结果 JSON。</summary>
    public string? ResultJson { get; set; }

    /// <summary>最近一次识别的图片字节。</summary>
    public byte[]? Image { get; set; }

    /// <summary>最近一次识别的图片 data URL。</summary>
    public string? ImageUrl { get; set; }
}
