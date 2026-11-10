namespace Snet.Yolo.Server.anomalib;

using SkiaSharp;
using Snet.Yolo.Server.models.data;
using Snet.Yolo.Server.models.@enum;
using System.Diagnostics;

/// <summary>一张图片的联合验证结果。</summary>
public sealed record JointValidationOutput(
    AnomalibInferenceOutput? Anomalib,
    JointMatchResult Matches,
    JointValidationMode Mode,
    bool YoloExecuted,
    long YoloMilliseconds);
