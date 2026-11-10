using Snet.Yolo.Server.anomalib;
using Snet.Yolo.Server.models.@enum;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Snet.VisualIdentity.JointVerificationDemo.Configuration;

/// <summary>集中定义两类模型、阈值、待识别图片及运行模式，不依赖 Web 页面配置。</summary>
public sealed class DemoOptions
{
    /// <summary>Anomalib 模型文件、清单与区域过滤参数。</summary>
    public AnomalibOptions Anomalib { get; init; } = new();
    /// <summary>YOLO 模型文件、实际模型类型及对应识别参数。</summary>
    public YoloOptions Yolo { get; init; } = new();
    /// <summary>待识别图片路径，相对路径以配置文件目录为基准。</summary>
    public string ImagePath { get; init; } = "images/test.jpg";
    /// <summary>与联合验证页面一致，支持联合识别、仅 Anomalib 和仅 YOLO。</summary>
    public JointValidationMode Mode { get; init; } = JointValidationMode.Joint;
    /// <summary>CUDA 构建使用的显卡编号；CPU 构建不使用该参数。</summary>
    public int GpuId { get; init; }
    /// <summary>可选中文字体文件；Linux 建议提供支持中文的字体以避免标签显示方框。</summary>
    public string? FontPath { get; init; }
    /// <summary>是否生成 Anomalib 热图；图片演示开启，实时平台可关闭以减少编码开销。</summary>
    public bool IncludeHeatmap { get; init; } = true;

    /// <summary>读取配置并将相对路径解析为绝对路径，识别前检查所需文件和参数。</summary>
    /// <param name="path">JSON 配置文件路径。</param>
    /// <param name="token">读取配置时的取消标记。</param>
    /// <returns>已校验、路径已解析的配置。</returns>
    public static async Task<DemoOptions> LoadAsync(string path, CancellationToken token = default)
    {
        path = Path.GetFullPath(path);
        await using var stream = File.OpenRead(path);
        var options = await JsonSerializer.DeserializeAsync<DemoOptions>(stream,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true, Converters = { new JsonStringEnumConverter() } }, token)
            ?? throw new InvalidDataException("配置文件不能为空。");
        var root = Path.GetDirectoryName(path)!;
        if (options.Anomalib is null || options.Yolo is null) { throw new InvalidDataException("两类模型配置不能为 null。"); }
        options = new DemoOptions
        {
            Anomalib = options.Anomalib with
            {
                OnnxPath = Path.GetFullPath(options.Anomalib.OnnxPath, root),
                ManifestPath = Path.GetFullPath(options.Anomalib.ManifestPath, root),
            },
            Yolo = options.Yolo with { OnnxPath = Path.GetFullPath(options.Yolo.OnnxPath, root) },
            ImagePath = Path.GetFullPath(options.ImagePath, root),
            Mode = options.Mode,
            GpuId = options.GpuId,
            IncludeHeatmap = options.IncludeHeatmap,
            FontPath = string.IsNullOrWhiteSpace(options.FontPath) ? null : Path.GetFullPath(options.FontPath, root),
        };
        options.Validate();
        return options;
    }

    /// <summary>拒绝无效阈值、错误模式和缺失文件，避免进入原生推理后才发现配置错误。</summary>
    public void Validate()
    {
        if (!Enum.IsDefined(Mode) || GpuId < 0) { throw new InvalidDataException("运行模式或显卡编号无效。"); }
        Anomalib.ToInferenceOptions().Validate();
        Yolo.Validate();
        RequireFile(ImagePath);
        if (Mode != JointValidationMode.YoloOnly) { RequireFile(Anomalib.OnnxPath); RequireFile(Anomalib.ManifestPath); }
        if (Mode != JointValidationMode.AnomalibOnly) { RequireFile(Yolo.OnnxPath); }
        if (FontPath is not null) { RequireFile(FontPath); }
    }

    /// <summary>确认配置中的文件存在，不创建或改写用户的模型文件。</summary>
    /// <param name="path">需要检查的文件路径。</param>
    private static void RequireFile(string path)
    {
        if (!File.Exists(path)) { throw new FileNotFoundException("配置中的文件不存在，请修改 demo.json 中的路径。", path); }
    }
}
