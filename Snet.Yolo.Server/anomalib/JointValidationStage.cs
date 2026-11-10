namespace Snet.Yolo.Server.anomalib;

using SkiaSharp;
using Snet.Yolo.Server.models.data;
using Snet.Yolo.Server.models.@enum;
using System.Diagnostics;

/// <summary>供调用方展示进度的推理阶段。</summary>
public enum JointValidationStage
{
    /// <summary>开始异常定位。</summary>
    AnomalibStarted,
    /// <summary>完成异常定位。</summary>
    AnomalibCompleted,
    /// <summary>开始 YOLO 识别。</summary>
    YoloStarted,
    /// <summary>完成 YOLO 识别。</summary>
    YoloCompleted,
    /// <summary>正常图片跳过 YOLO。</summary>
    YoloSkippedNormal,
}
