namespace Snet.Yolo.Server.inference;
/// <summary>TensorRT 引擎生成、精度及缓存配置。</summary>
public record TensorRt()
{
    /// <summary>TensorRT 运算精度。</summary>
    public TrtPrecision? Precision { get; init; }
    // Precision mode to use: FP32, FP16, or INT8 (requires calibration).

    /// <summary>引擎构建优化级别，范围 0–5。</summary>
    public int BuilderOptimizationLevel { get; init; } = 3;
    // Optimization level for building the TensorRT engine (0-5).
    // Higher levels spend more time optimizing but increase build time.
    // Levels below 3 may reduce engine performance.

    /// <summary>引擎及形状配置缓存目录。</summary>
    public string? EngineCachePath { get; init; }
    // Directory path to load/store TensorRT engine and profile cache files.

    /// <summary>生成的引擎缓存文件前缀。</summary>
    public string? EngineCachePrefix { get; init; }
    // Optional prefix for generated engine cache files.
    // Defaults to a standard prefix if not set.

    /// <summary>INT8 执行所需的校准缓存文件。</summary>
    public string? Int8CalibrationCacheFile { get; init; }
    // Required only for INT8 precision mode.
    // Path to the INT8 calibration cache file used to assign tensor dynamic ranges.
    // Must be generated beforehand via calibration (see demo notes).
}
