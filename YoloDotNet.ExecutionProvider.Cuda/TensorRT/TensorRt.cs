namespace YoloDotNet.ExecutionProvider.Cuda.TensorRT
{
    /// <summary>Configures optional TensorRT engine generation and caching.</summary>
    public record TensorRt()
    {
        /// <summary>Gets the requested TensorRT arithmetic precision.</summary>
        public TrtPrecision? Precision { get; init; }
        // Precision mode to use: FP32, FP16, or INT8 (requires calibration).

        /// <summary>Gets the TensorRT builder optimization level from zero through five.</summary>
        public int BuilderOptimizationLevel { get; init; } = 3;
        // Optimization level for building the TensorRT engine (0-5).
        // Higher levels spend more time optimizing but increase build time.
        // Levels below 3 may reduce engine performance.

        /// <summary>Gets the directory used for TensorRT engine and profile caches.</summary>
        public string? EngineCachePath { get; init; }
        // Directory path to load/store TensorRT engine and profile cache files.

        /// <summary>Gets the optional prefix for generated TensorRT cache files.</summary>
        public string? EngineCachePrefix { get; init; }
        // Optional prefix for generated engine cache files.
        // Defaults to a standard prefix if not set.

        /// <summary>Gets the calibration cache file required for INT8 execution.</summary>
        public string? Int8CalibrationCacheFile { get; init; }
        // Required only for INT8 precision mode.
        // Path to the INT8 calibration cache file used to assign tensor dynamic ranges.
        // Must be generated beforehand via calibration (see demo notes).
    }
}
