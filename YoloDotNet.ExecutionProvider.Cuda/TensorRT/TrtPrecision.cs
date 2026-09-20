namespace YoloDotNet.ExecutionProvider.Cuda.TensorRT
{
    /// <summary>Specifies the arithmetic precision used by TensorRT.</summary>
    public enum TrtPrecision
    {
        /// <summary>32-bit floating-point precision.</summary>
        FP32,
        /// <summary>16-bit floating-point precision.</summary>
        FP16,
        /// <summary>8-bit integer precision with calibration.</summary>
        INT8
    }
}
