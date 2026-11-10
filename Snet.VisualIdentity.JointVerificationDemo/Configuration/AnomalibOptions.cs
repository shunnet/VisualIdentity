using Snet.Yolo.Server.anomalib;
using Snet.Yolo.Server.models.@enum;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Snet.VisualIdentity.JointVerificationDemo.Configuration;

/// <summary>Anomalib 必须同时提供 ONNX 和本项目导出的部署清单，不能仅提供裸模型。</summary>
public sealed record AnomalibOptions
{
    /// <summary>Anomalib ONNX 文件路径。</summary>
    public string OnnxPath { get; init; } = "models/anomalib/model.onnx";
    /// <summary>部署清单路径，包含摘要、输入归一化及输出节点契约。</summary>
    public string ManifestPath { get; init; } = "models/anomalib/model.manifest.json";
    /// <summary>区域异常阈值，不改变模型的整图异常判定。</summary>
    public float PixelThreshold { get; init; } = 0.8f;
    /// <summary>最小异常面积，单位为异常图像素，而非原图像素。</summary>
    public int MinimumArea { get; init; } = 4;
    /// <summary>转换为 Server 实际使用的参数类型。</summary>
    public AnomalibInferenceOptions ToInferenceOptions() => new() { PixelThreshold = PixelThreshold, MinimumArea = MinimumArea };
}
