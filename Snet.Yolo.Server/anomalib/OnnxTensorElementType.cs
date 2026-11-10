using System.Text.Json.Serialization;

namespace Snet.Yolo.Server.anomalib;

/// <summary>
/// ONNX 张量元素类型。
/// </summary>
public enum OnnxTensorElementType
{
    /// <summary>32 位浮点数。</summary>
    Float32,

    /// <summary>64 位浮点数。</summary>
    Float64,

    /// <summary>64 位有符号整数。</summary>
    Int64,

    /// <summary>8 位无符号整数。</summary>
    UInt8,

    /// <summary>布尔值。</summary>
    Boolean
}
