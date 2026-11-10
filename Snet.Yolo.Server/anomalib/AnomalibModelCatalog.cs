namespace Snet.Yolo.Server.anomalib;

using System.Text.Json;
using System.Text.Json.Serialization;

/// <summary>受支持模型的集中映射，避免页面、服务与 Python 脚本各自维护名称。</summary>
public static class AnomalibModelCatalog
{
    /// <summary>获取指定模型的不可变描述。</summary>
    /// <param name="kind">模型类型。</param>
    /// <returns>模型描述。</returns>
    public static AnomalibModelDescriptor Get(AnomalibModelKind kind) => kind switch
    {
        AnomalibModelKind.Padim => new(kind, "padim", "PaDiM", false),
        AnomalibModelKind.EfficientAdSmall => new(kind, "efficient_ad_small", "EfficientAD Small", false),
        AnomalibModelKind.PatchCore => new(AnomalibModelKind.PatchCore, "patchcore", "PatchCore", false),
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "不支持的 Anomalib 模型。"),
    };
}
