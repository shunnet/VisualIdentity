using YoloDotNet.ExecutionProvider.Cuda;
using YoloDotNet.Models.Interfaces;

namespace Snet.Yolo.Tasks.Services;

/// <summary>
/// 创建 CUDA GPU 推理执行提供程序；CUDA 运行库不可用时自动降级为 CPU 并给出说明，
/// 而不是让用户看到 “Failed to load library libonnxruntime_providers_cuda.so ... libcublasLt.so.12” 这类原生错误。
/// </summary>
internal sealed class ExecutionProviderFactory : IExecutionProviderFactory
{
    private readonly ILogger<ExecutionProviderFactory> _logger;
    private readonly Lazy<string?> _degradedReason;
    private string? _runtimeDegradedReason;
    private int _noticeLogged;

    public ExecutionProviderFactory(ILogger<ExecutionProviderFactory> logger)
    {
        _logger = logger;
        _degradedReason = new Lazy<string?>(PrepareCuda, LazyThreadSafetyMode.ExecutionAndPublication);
    }

    /// <inheritdoc />
    public string? HardwareNotice
    {
        get
        {
            var reason = Volatile.Read(ref _runtimeDegradedReason) ?? _degradedReason.Value;
            return reason is null ? null : "CUDA 推理不可用，已自动改用 CPU 推理。" + reason;
        }
    }

    /// <inheritdoc />
    public IExecutionProvider Create(string modelPath)
    {
        var reason = Volatile.Read(ref _runtimeDegradedReason) ?? _degradedReason.Value;
        if (reason is null)
        {
            try { return new CudaExecutionProvider(modelPath, 0, null); }
            catch (Exception error) when (IsCudaAvailabilityFailure(error))
            {
                reason = "CUDA 会话初始化失败：" + error.Message;
                Interlocked.CompareExchange(ref _runtimeDegradedReason, reason, null);
            }
        }
        if (Interlocked.Exchange(ref _noticeLogged, 1) == 0)
        {
            _logger.LogWarning("CUDA 执行提供程序不可用，已降级为 CPU 推理：{Reason}", reason);
        }

        // GPU 版 ONNX Runtime 本身包含 CPU 执行路径。复用同一原生运行库可避免同时
        // 发布 CPU/GPU 两套 onnxruntime.dll，后者会造成 CUDA 入口被 CPU DLL 覆盖。
        return new CudaExecutionProvider(modelPath, -1, null);
    }

    /// <summary>仅把 CUDA 运行环境问题降级；模型损坏等业务错误仍原样抛出。</summary>
    private static bool IsCudaAvailabilityFailure(Exception error)
    {
        for (Exception? current = error; current is not null; current = current.InnerException)
        {
            var message = current.Message;
            if (message.Contains("CUDA execution provider", StringComparison.OrdinalIgnoreCase)
                || message.Contains("onnxruntime_providers_cuda", StringComparison.OrdinalIgnoreCase)
                || message.Contains("cublas", StringComparison.OrdinalIgnoreCase)
                || message.Contains("cudnn", StringComparison.OrdinalIgnoreCase)
                || message.Contains("CUDA failure", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }
        return false;
    }

    /// <summary>尝试让 CUDA 运行库可被 ONNX Runtime 找到；返回 null 表示可用，否则返回原因。</summary>
    private static string? PrepareCuda()
    {
        try
        {
            return CudaRuntimeLibraries.TryPrepare(
                AppContext.BaseDirectory,
                message => Console.WriteLine("[cuda] " + message));
        }
        catch (Exception error)
        {
            // 任何异常都必须降级而不是让推理直接失败
            return "准备 CUDA 运行库时出错：" + error.Message;
        }
    }
}
