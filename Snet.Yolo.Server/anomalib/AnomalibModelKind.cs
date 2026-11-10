namespace Snet.Yolo.Server.anomalib;

using System.Text.Json;
using System.Text.Json.Serialization;

/// <summary>第一阶段支持的 Anomalib 训练模型。</summary>
public enum AnomalibModelKind
{
    /// <summary>稳定优先的 PaDiM 模型。</summary>
    Padim,

    /// <summary>速度优先的 EfficientAD Small 模型。</summary>
    EfficientAdSmall,

    /// <summary>基于正常图像特征库的 PatchCore 模型。</summary>
    PatchCore,

    /// <summary>兼容旧接口和已保存的 PatchCore 实验类型。</summary>
    PatchcoreExperimental = PatchCore,
}
