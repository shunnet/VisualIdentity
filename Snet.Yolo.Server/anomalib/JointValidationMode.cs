namespace Snet.Yolo.Server.anomalib;

using SkiaSharp;
using Snet.Yolo.Server.models.data;
using Snet.Yolo.Server.models.@enum;
using System.Diagnostics;

/// <summary>联合验证模式。</summary>
public enum JointValidationMode
{
    /// <summary>先定位异常，再识别已知缺陷。</summary>
    Joint,
    /// <summary>仅定位异常。</summary>
    AnomalibOnly,
    /// <summary>仅识别已知缺陷。</summary>
    YoloOnly,
}
