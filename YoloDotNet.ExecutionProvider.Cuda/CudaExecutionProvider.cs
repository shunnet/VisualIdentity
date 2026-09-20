// SPDX-License-Identifier: MIT
// SPDX-FileCopyrightText: 2025-2026 Niklas Swärd
// https://github.com/NickSwardh/YoloDotNet

using System.Buffers;

namespace YoloDotNet.ExecutionProvider.Cuda
{
    /// <summary>Runs ONNX inference with CUDA and optional TensorRT acceleration.</summary>
    public class CudaExecutionProvider : IExecutionProvider, IDisposable
    {
        /// <summary>Gets parsed metadata for the loaded ONNX model.</summary>
        public OnnxModel OnnxData { get; private set; } = default!;
        /// <summary>Gets the underlying ONNX Runtime session.</summary>
        public object Session => _session;

        #region Private Fields
        private InferenceSession _session = default!;
        private RunOptions _runOptions = default!;

        private long[] _inputShape = default!;
        private string[] _inputNames = default!;
        private int _inputShapeSize;
        private List<string> _outputNames = default!;
        private TensorElementType[] _outputElementTypes = default!;
        private int _dataTypeSize;
        private TensorElementType _elementDataType = default!;

        private int _disposed;
        private readonly object _sessionLock = new();

        #endregion

        #region Constructors
        /// <summary>
        /// Constructs a CudaExecutionProvider for running ONNX models using CUDA and optionally TensorRT.
        /// </summary>
        /// <param name="model"></param>
        /// <param name="gpuId"></param>
        /// <param name="trtConfig"></param>
        public CudaExecutionProvider(string model, int gpuId = 0, TensorRt? trtConfig = null)
        {
            InitializeYolo(model, gpuId, trtConfig);
        }

        /// <summary>
        /// Overload: Constructs a CudaExecutionProvider for running ONNX models using CUDA and optionally TensorRT.
        /// </summary>
        /// <param name="model"></param>
        /// <param name="gpuId"></param>
        /// <param name="trtConfig"></param>
        public CudaExecutionProvider(byte[] model, int gpuId = 0, TensorRt? trtConfig = null)
        {
            InitializeYolo(model, gpuId, trtConfig);
        }
        #endregion

        #region Initialization
        /// <summary>
        /// Initializes the ONNX Runtime session, configures the CUDA execution provider and allocates resources.
        /// </summary>
        /// <param name="model"></param>
        /// <param name="gpuId"></param>
        /// <param name="trtConfig"></param>
        private void InitializeYolo(object model, int gpuId, TensorRt? trtConfig)
        {
            ConfigureOrtEnv();

            using var options = CreateSessionOptions(gpuId, trtConfig);

            // Create session using bytes if available; else load from file with selected provider.
            _session = (model is byte[] modelBytes)
                ? new InferenceSession(modelBytes, options)
                : new InferenceSession((string)model, options);

            try
            {
                GetOnnxMetaData();
                InitializeInferenceParameters();
                _runOptions = new RunOptions();
            }
            catch
            {
                _session.Dispose();
                throw;
            }

        }

        private void InitializeInferenceParameters()
        {
            var firstInput = OnnxData.InputShapes.FirstOrDefault();

            if (EqualityComparer<KeyValuePair<string, long[]>>.Default.Equals(firstInput, default))
                throw new YoloDotNetException("Corrupt or incompatible model. No input shape was found.");

            _inputNames = [firstInput.Key];
            _inputShape = firstInput.Value;
            _inputShapeSize = OnnxData.InputShapeSize;
            _outputNames = [.. OnnxData.OutputShapes.Select(x => x.Key)];
            _outputElementTypes = [.. _outputNames.Select(name => _session.OutputMetadata[name].ElementDataType)];

            if (OnnxData.ModelDataType == ModelDataType.Float)
            {
                _elementDataType = TensorElementType.Float;
                _dataTypeSize = sizeof(float);
            }
            else
            {
                _elementDataType = TensorElementType.Float16;
                _dataTypeSize = sizeof(ushort);
            }
        }

        #endregion

        #region Run Inference
        /// <summary>
        /// Runs inference on the provided normalized pixel data.
        /// </summary>
        /// <param name="normalizedPixels"></param>
        unsafe public InferenceResult Run<T>(T[] normalizedPixels) where T : unmanaged
        {
            lock (_sessionLock)
            {
                ObjectDisposedException.ThrowIf(_disposed != 0, this);
                ValidateInput(normalizedPixels);

                // Pin the input pixel data in memory to prevent it from being moved by the garbage collector.
                fixed (T* pData = normalizedPixels)
                {
                // Create an OrtValue tensor from the pinned data
                using var inputOrtValue = OrtValue.CreateTensorValueWithData(
                    OrtMemoryInfo.DefaultInstance,
                    _elementDataType,
                    _inputShape,
                    (IntPtr)pData,
                    checked((long)_inputShapeSize * _dataTypeSize)
                );

                var result = _session.Run(
                    _runOptions,
                    _inputNames,
                    [inputOrtValue],
                    _outputNames);

                if (result.Count is < 1 or > 2 || result.Count != _outputNames.Count)
                {
                    var actualOutputCount = result.Count;
                    result.Dispose();
                    throw new YoloDotNetException($"Expected {_outputNames.Count} model outputs but received {actualOutputCount}.");
                }

                var outputElementType = _outputElementTypes[0];
                if (_outputElementTypes.Any(type => type != outputElementType))
                {
                    result.Dispose();
                    throw new YoloDotNetException("Model outputs with mixed tensor element types are not supported.");
                }

                if (outputElementType == TensorElementType.UInt8)
                {
                    if (result.Count != 1)
                    {
                        result.Dispose();
                        throw new YoloDotNetException("UInt8 model output is supported only for single-output models.");
                    }

                    try
                    {
                        return new InferenceResult(result[0].GetTensorDataAsSpan<byte>(), result);
                    }
                    catch
                    {
                        result.Dispose();
                        throw;
                    }
                }

                if (outputElementType == TensorElementType.Float)
                {
                    try
                    {
                        var tensorData0 = result[0].GetTensorDataAsSpan<float>();
                        var tensorData1 = result.Count == 2
                            ? result[1].GetTensorDataAsSpan<float>()
                            : ReadOnlySpan<float>.Empty;

                        return new InferenceResult(tensorData0, tensorData1, result);
                    }
                    catch
                    {
                        result.Dispose();
                        throw;
                    }
                }

                if (outputElementType != TensorElementType.Float16)
                {
                    result.Dispose();
                    throw new YoloDotNetException($"Unsupported model output element type '{outputElementType}'.");
                }

                try
                {
                    var tensorData0 = result[0].GetTensorDataAsSpan<Float16>();
                    var tensorData1 = ReadOnlySpan<Float16>.Empty;

                    if (result.Count == 2)
                        tensorData1 = result[1].GetTensorDataAsSpan<Float16>();

                    var owner = PooledFloatOutputs.Create(tensorData0, tensorData1);
                    return new InferenceResult(owner.Output0, owner.Output1, owner);
                }
                finally
                {
                    result.Dispose();
                }
            }
        }
        }
        #endregion

        #region CUDA and TensorRT helper methods
        /// <summary>Validates that an input buffer matches the model tensor type and size.</summary>
        private void ValidateInput<T>(T[] normalizedPixels) where T : unmanaged
        {
            ArgumentNullException.ThrowIfNull(normalizedPixels);

            var expectedType = _elementDataType == TensorElementType.Float ? typeof(float) : typeof(ushort);
            if (typeof(T) != expectedType)
                throw new ArgumentException($"The model expects elements of type {expectedType.Name}, not {typeof(T).Name}.", nameof(normalizedPixels));

            if (normalizedPixels.Length < _inputShapeSize)
                throw new ArgumentException($"The input requires at least {_inputShapeSize} elements.", nameof(normalizedPixels));
        }

        /// <summary>
        /// Creates and configures session options for the ONNX Runtime session.
        /// </summary>
        private SessionOptions CreateSessionOptions(int gpuId, TensorRt? trtConfig)
        {
            var options = new SessionOptions
            {
                GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL,
                ExecutionMode = ExecutionMode.ORT_SEQUENTIAL
            };

            if (gpuId >= 0)
            {
                if (trtConfig is not null)
                {
                    options.ConfigureTensorRT(gpuId, trtConfig);
                }
                else
                {
                    ConfigureCuda(gpuId, options);
                }
            }
            else if (gpuId == -1)
            {
                options.EnableCpuMemArena = true;
            }
            else
            {
                throw new ArgumentOutOfRangeException(
                    paramName: nameof(gpuId),
                    actualValue: gpuId,
                    message: "The specified gpuId is not valid. Use -1 for CPU execution, or 0 and above for a GPU device ID.");
            }

            return options;
        }

        /// <summary>
        /// Configure the global OrtEnv instance with custom logging options.
        /// </summary>
        private static void ConfigureOrtEnv()
        {
            try
            {
                // Log errors and fatals
                var envOptions = new EnvironmentCreationOptions
                {
                    logLevel = OrtLoggingLevel.ORT_LOGGING_LEVEL_ERROR
                };

                OrtEnv.CreateInstanceWithOptions(ref envOptions);
            }
            catch (OnnxRuntimeException ex) when (ex.Message.Contains("OrtEnv singleton instance already exists"))
            {
                // OrtEnv has already been initialized — ignore and continue gracefully...
            }
        }

        /// <summary>
        /// Configures the session options to use the CUDA execution provider with specified options.
        /// </summary>
        private static void ConfigureCuda(int gpuId, SessionOptions options)
        {
            using var cudaOptions = new OrtCUDAProviderOptions();

            cudaOptions.UpdateOptions(new Dictionary<string, string>
            {
                { "device_id", gpuId.ToString() },
                // Specifies which GPU device to use (default = 0 if not set).

                { "arena_extend_strategy", "kNextPowerOfTwo" }, 
                // Controls how the GPU memory arena grows when more memory is needed.
                // kNextPowerOfTwo doubles the allocation size to the next power of two,
                // which reduces the frequency of CUDA malloc/free calls and minimizes fragmentation 
                // in long-running or high-throughput inference scenarios like YOLO object detection.

                { "cudnn_conv_algo_search", "EXHAUSTIVE" },
                // Forces cuDNN to benchmark all available convolution algorithms during model initialization
                // and select the fastest one for the hardware + model combination.
                // This gives optimal conv kernel performance at runtime, especially beneficial for large or custom conv layers.

                // Reduce host/device synchronization by enabling copy using the default stream when supported.
                // This can avoid implicit stream synchronizations on some ONNX Runtime builds.
                { "do_copy_in_default_stream", "1" },

                // Allow cuDNN to use the maximum workspace (may increase memory usage but can improve kernel perf).
                { "cudnn_conv_use_max_workspace", "1" }

            });

            options.AppendExecutionProvider_CUDA(cudaOptions);
        }

        /// <summary>Owns pooled Float32 buffers converted from Float16 model outputs.</summary>
        private sealed class PooledFloatOutputs : IDisposable
        {
            private float[]? _buffer0;
            private float[]? _buffer1;

            /// <summary>Gets the converted first output.</summary>
            public ReadOnlySpan<float> Output0 => _buffer0.AsSpan(0, Length0);

            /// <summary>Gets the converted optional second output.</summary>
            public ReadOnlySpan<float> Output1 => _buffer1.AsSpan(0, Length1);

            private int Length0 { get; init; }
            private int Length1 { get; init; }

            /// <summary>Converts model outputs into rented Float32 buffers.</summary>
            public static PooledFloatOutputs Create(ReadOnlySpan<Float16> source0, ReadOnlySpan<Float16> source1)
            {
                var owner = new PooledFloatOutputs
                {
                    _buffer0 = ArrayPool<float>.Shared.Rent(source0.Length),
                    _buffer1 = source1.IsEmpty ? null : ArrayPool<float>.Shared.Rent(source1.Length),
                    Length0 = source0.Length,
                    Length1 = source1.Length
                };

                for (var i = 0; i < source0.Length; i++)
                    owner._buffer0[i] = (float)source0[i];

                for (var i = 0; i < source1.Length; i++)
                    owner._buffer1![i] = (float)source1[i];

                return owner;
            }

            /// <summary>Returns the converted output buffers to the shared pool.</summary>
            public void Dispose()
            {
                var buffer0 = Interlocked.Exchange(ref _buffer0, null);
                var buffer1 = Interlocked.Exchange(ref _buffer1, null);

                if (buffer0 is not null)
                    ArrayPool<float>.Shared.Return(buffer0);
                if (buffer1 is not null)
                    ArrayPool<float>.Shared.Return(buffer1);
            }
        }

        /// <summary>
        /// Extracts metadata and input/output shapes from the ONNX model.
        /// </summary>
        private void GetOnnxMetaData()
            => OnnxData = _session.ParseOnnx();

        /// <summary>Releases the run options and ONNX Runtime session.</summary>
        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
                return;

            lock (_sessionLock)
            {
                _runOptions?.Dispose();
                _session?.Dispose();
            }
            GC.SuppressFinalize(this);
        }
        #endregion
    }
}
