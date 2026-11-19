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
    long YoloMilliseconds)
{
    /// <summary>所有 YOLO 目标，包括已关联和未关联异常区域的目标。</summary>
    public IEnumerable<JointDetection> YoloDetections
        => Matches.Regions.SelectMany(region => region.Detections).Concat(Matches.UnmatchedDetections);
}
