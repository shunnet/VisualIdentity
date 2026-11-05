namespace Snet.Yolo.Server.inference;
/// <summary>TensorRT 运算精度选项。</summary>
public enum TrtPrecision
{
    /// <summary>32 位浮点精度。</summary>
    FP32,
    /// <summary>16 位浮点精度。</summary>
    FP16,
    /// <summary>需要校准的 8 位整数精度。</summary>
    INT8
}
