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

    /// <summary>获取指定模型的输入尺寸建议（训练页据此给出推荐值并在明显偏大时提示）。</summary>
    /// <param name="kind">模型类型。</param>
    /// <returns>推荐尺寸与建议上限。</returns>
    public static AnomalibSizeGuidance GetSizeGuidance(AnomalibModelKind kind) => kind switch
    {
        // PaDiM：实测 256 px 最好，越大越差，显存按边长平方增长。
        AnomalibModelKind.Padim => new(256, 384),
        // EfficientAD：网络结构按 256 px 设计，更大尺寸训练更慢，没有证据表明更准。
        AnomalibModelKind.EfficientAdSmall => new(256, 512),
        // PatchCore：384–512 px 区分能力最好且最划算，640 px 起不再提升。
        AnomalibModelKind.PatchCore => new(512, 768),
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "不支持的 Anomalib 模型。"),
    };
}
