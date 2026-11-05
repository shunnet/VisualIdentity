// SPDX-License-Identifier: MIT
// SPDX-FileCopyrightText: 2025 Niklas Swärd
// https://github.com/NickSwardh/YoloDotNet

using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using Snet.Yolo.Server.yolo.Core;
using Snet.Yolo.Server.yolo.Enums;
using Snet.Yolo.Server.yolo.Exceptions;
using Snet.Yolo.Server.yolo.Extensions;
using Snet.Yolo.Server.yolo.Models;
using Snet.Yolo.Server.yolo.Models.Interfaces;

namespace Snet.Yolo.Server.inference;

/// <summary>CPU 与 CUDA 共享的张量推理、缓冲区所有权及会话生命周期实现。</summary>
public abstract class OnnxExecutionProvider : IExecutionProvider, IDisposable
{
    /// <summary>已加载模型的元数据与输入输出契约。</summary>
    public OnnxModel OnnxData { get; private set; } = default!;
    /// <summary>当前原生会话；调用方不得自行释放。</summary>
    public object Session => _session;

    #region Private Fields
    private InferenceSession _session = default!;
    private RunOptions _runOptions = default!;

    private string[] _inputNames = default!;
    private long[] _inputShape = default!;
    private int _inputShapeSize;
    private List<string> _outputNames = default!;
    private TensorElementType[] _outputElementTypes = default!;
    private TensorElementType _elementDataType = default!;
    private readonly object _sessionLock = new();
    private int _disposed;
    #endregion

    /// <summary>使用指定会话选项创建模型；初始化结束后释放选项。</summary>
    /// <param name="model">模型绝对路径或 ONNX 字节数组。</param>
    /// <param name="options">由构造函数取得所有权的会话选项。</param>
    protected OnnxExecutionProvider(object model, SessionOptions options)
    {
        using (options) { InitializeYolo(model, options); }
    }

    #region Initialization
    /// <summary>创建原生会话及固定推理参数；后续初始化失败时释放会话。</summary>
    /// <param name="model">模型绝对路径或 ONNX 字节数组。</param>
    /// <param name="options">已按目标硬件配置的会话选项。</param>
    private void InitializeYolo(object model, SessionOptions options)
    {
        ConfigureOrtEnv();

        // 按调用方提供的内存模型或文件路径创建同一种硬件会话。
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

    /// <summary>缓存张量名称、形状及类型，避免逐帧重复解析元数据。</summary>
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
        }
        else
        {
            _elementDataType = TensorElementType.Float16;
        }
    }
    #endregion

    #region Run Inference
    /// <summary>
    /// 在会话锁内执行归一化张量推理；结果持有原生输出或池化缓冲区。
    /// </summary>
    /// <typeparam name="T">模型输入元素类型。</typeparam>
    /// <param name="normalizedPixels">与模型形状、类型匹配的归一化张量数据。</param>
    /// <returns>用完必须释放的推理结果。</returns>
    unsafe public InferenceResult Run<T>(T[] normalizedPixels) where T : unmanaged
    {
        lock (_sessionLock)
        {
            ObjectDisposedException.ThrowIf(_disposed != 0, this);
            ValidateInput(normalizedPixels);

            fixed (T* pData = normalizedPixels)
            {
                using var inputOrtValue = OrtValue.CreateTensorValueWithData(
                    OrtMemoryInfo.DefaultInstance,
                    _elementDataType,
                    _inputShape,
                    (IntPtr)pData,
                    checked((long)_inputShapeSize * sizeof(T))
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

    #region Helper methods

    /// <summary>
    /// Configures the ONNX Runtime environment to log errors and fatals only.
    /// </summary>
    /// <summary>设置全局 ONNX Runtime 错误日志等级；已有环境保持原样。</summary>
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

    /// <summary>验证输入缓冲区类型及元素数，防止越界读取。</summary>
    /// <typeparam name="T">输入元素类型。</typeparam>
    /// <param name="normalizedPixels">待校验的输入张量缓冲区。</param>
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
    /// 从会话读取模型元数据及输入输出形状。
    /// </summary>
    private void GetOnnxMetaData()
        => OnnxData = _session.ParseOnnx();

    /// <summary>
    /// Gets the tensor element type used by the model (e.g., Float32 or Float16).
    /// </summary>
    internal TensorElementType GetModelElementType()
        => _session.InputMetadata["images"].ElementDataType;

    /// <summary>幂等释放选项与会话；等待正在运行的推理退出会话锁。</summary>
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
