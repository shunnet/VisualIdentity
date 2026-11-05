// SPDX-License-Identifier: MIT
// SPDX-FileCopyrightText: 2025-2026 Niklas Swärd
// https://github.com/NickSwardh/YoloDotNet

using Microsoft.ML.OnnxRuntime;

namespace Snet.Yolo.Server.inference;

/// <summary>集中配置 CPU、CUDA 和 TensorRT；不加载其他原生运行库版本。</summary>
public static class OnnxSessionOptions
{
    /// <summary>当前已加载的原生运行库是否包含 CUDA；不代表驱动或指定 GPU 已就绪。</summary>
    public static bool SupportsCuda => OrtEnv.Instance().GetAvailableProviders().Contains("CUDAExecutionProvider", StringComparer.Ordinal);

    /// <summary>创建经过图优化的 CPU 会话选项，由调用方释放。</summary>
    /// <returns>只使用默认 CPU 执行提供程序的选项。</returns>
    public static SessionOptions CreateCpu() => new()
    {
        GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL,
        ExecutionMode = ExecutionMode.ORT_SEQUENTIAL,
        EnableCpuMemArena = true,
    };

    /// <summary>创建指定 GPU 的会话选项；失败释放选项并返回原始错误。</summary>
    /// <param name="gpuId">GPU 编号，-1 表示显式 CPU。</param>
    /// <param name="trtConfig">可选 TensorRT 引擎配置。</param>
    /// <returns>由调用方释放的完整硬件选项。</returns>
    public static SessionOptions CreateCuda(int gpuId, TensorRt? trtConfig = null)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(gpuId, -1);
        var options = CreateCpu();
        try
        {
            if (gpuId >= 0)
            {
                if (trtConfig is null) { ConfigureCuda(gpuId, options); }
                else { options.ConfigureTensorRT(gpuId, trtConfig); }
            }
            return options;
        }
        catch { options.Dispose(); throw; }
    }

    /// <summary>配置指定 GPU 的内存池及卷积优化。</summary>
    /// <param name="gpuId">非负 GPU 编号。</param>
    /// <param name="options">待追加 CUDA 执行提供程序的选项。</param>
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
}
