namespace Snet.Yolo.Server.anomalib;

using SkiaSharp;
using Snet.Yolo.Server.models.data;
using Snet.Yolo.Server.models.@enum;
using System.Diagnostics;

/// <summary>当前用户可用于联合验证的模型。</summary>
public sealed record JointValidationModels(IReadOnlyList<RegisteredAnomalibModel> Anomalib, IReadOnlyList<OnnxData> Yolo);
