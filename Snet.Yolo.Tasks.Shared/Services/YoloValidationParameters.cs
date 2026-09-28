using Snet.Yolo.Server.models.@enum;

namespace Snet.Yolo.Tasks.Services;

/// <summary>YOLO 验证和联合验证共用的按模型类型动态生成的数值参数。</summary>
public static class YoloValidationParameters
{
    public static Dictionary<string, double> Create(OnnxType? type) => type switch
    {
        OnnxType.Classification => new() { ["Classes"] = 1 },
        OnnxType.Segmentation => new() { ["Confidence"] = 0.2, ["Iou"] = 0.7, ["PixelConfidence"] = 0.65 },
        _ => new() { ["Confidence"] = 0.2, ["Iou"] = 0.7 },
    };
}
