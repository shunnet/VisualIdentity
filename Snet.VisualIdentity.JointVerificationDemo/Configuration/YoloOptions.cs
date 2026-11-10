using Snet.Yolo.Server.anomalib;
using Snet.Yolo.Server.models.@enum;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Snet.VisualIdentity.JointVerificationDemo.Configuration;

/// <summary>YOLO 仅支持联合验证页面允许的目标检测和实例分割。</summary>
public sealed record YoloOptions
{
    /// <summary>YOLO ONNX 文件路径，不是训练用的 PT 权重。</summary>
    public string OnnxPath { get; init; } = "models/yolo/model.onnx";
    /// <summary>模型任务类型，必须与导出的 ONNX 一致。</summary>
    public OnnxType Type { get; init; } = OnnxType.ObjectDetection;
    /// <summary>最低目标置信度。</summary>
    public double Confidence { get; init; } = 0.25;
    /// <summary>非极大值抑制的交并比阈值。</summary>
    public double Iou { get; init; } = 0.45;
    /// <summary>实例分割的像素置信度；目标检测模式不使用此参数。</summary>
    public double PixelConfidence { get; init; } = 0.65;
    /// <summary>校验任务类型和有限的零到一阈值。</summary>
    public void Validate()
    {
        if (Type is not (OnnxType.ObjectDetection or OnnxType.Segmentation)) { throw new InvalidDataException("YOLO 仅支持目标检测或实例分割。"); }
        if (new[] { Confidence, Iou, PixelConfidence }.Any(value => !double.IsFinite(value) || value is < 0 or > 1))
        { throw new InvalidDataException("YOLO 阈值必须是 0 到 1 之间的有限数值。"); }
    }
}
