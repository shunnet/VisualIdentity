using Snet.Yolo.Server.models.@enum;

namespace Snet.Yolo.Tasks.Services;

/// <summary>YOLO 验证和联合验证共用的按模型类型动态生成的数值参数。</summary>
public static class YoloValidationParameters
{
    /// <summary>加载该模型类型允许的参数，忽略旧配置中已不适用的选项。</summary>
    public static Dictionary<string, double> Restore(OnnxType? type, IReadOnlyDictionary<string, double>? saved)
    {
        var parameters = Create(type);
        if (saved is null) { return parameters; }
        foreach (var key in parameters.Keys.ToArray())
        {
            if (saved.TryGetValue(key, out var value) && double.IsFinite(value))
            {
                parameters[key] = key == "Classes" ? Math.Max(1, Math.Round(value)) : Math.Clamp(value, 0, 1);
            }
        }
        return parameters;
    }

    /// <summary>生成与推理类型对应的默认参数。</summary>
    public static Dictionary<string, double> Create(OnnxType? type) => type switch
    {
        OnnxType.Classification => new() { ["Classes"] = 1 },
        OnnxType.Segmentation => new() { ["Confidence"] = 0.2, ["Iou"] = 0.7, ["PixelConfidence"] = 0.65 },
        _ => new() { ["Confidence"] = 0.2, ["Iou"] = 0.7 },
    };
}
