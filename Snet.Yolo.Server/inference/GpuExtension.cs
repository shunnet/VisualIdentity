using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using System.Runtime.InteropServices;
using YoloDotNet.Core;
using YoloDotNet.Exceptions;

// SPDX-License-Identifier: MIT
// SPDX-FileCopyrightText: 2023-2026 Niklas Swärd
// https://github.com/NickSwardh/YoloDotNet

namespace Snet.Yolo.Server.inference;
/// <summary>ONNX Runtime 的张量内存绑定辅助方法。</summary>
public static class GpuExtension
{
    /// <summary>
    /// 分配输入绑定内存并同步执行；调用方负责释放返回的非托管指针。
    /// </summary>
    /// <param name="session">待运行的原生会话。</param>
    /// <param name="ortIoBinding">输入输出绑定。</param>
    /// <param name="runOptions">本次执行选项。</param>
    /// <param name="tensorElementType">模型输入元素类型。</param>
    /// <returns>必须使用 Marshal.FreeHGlobal 释放的内存地址。</returns>
    public static nint AllocateGpuMemory(this InferenceSession session,
        OrtIoBinding ortIoBinding,
        RunOptions runOptions,
        TensorElementType tensorElementType)
    {
        // Get input shape.
        var inputShape = Array.ConvertAll(session.InputMetadata[session.InputNames[0]].Dimensions, Convert.ToInt64);

        // Determine byte size based on model data type.
        var byteSize = tensorElementType switch
        {
            TensorElementType.Float => sizeof(float),
            TensorElementType.Float16 => sizeof(ushort),
            _ => throw new NotSupportedException($"Tensor element type {tensorElementType} is not supported by YoloDotNet.")
        };

        // Calculate input size and reject values that cannot be represented by AllocHGlobal.
        var inputSizeInBytes = checked(ShapeUtils.GetSizeForShape(inputShape) * byteSize);
        if (inputSizeInBytes <= 0 || inputSizeInBytes > nint.MaxValue)
            throw new YoloDotNetException("The model input tensor size is invalid or too large for unmanaged memory.");

        // Allocates unmanaged memory.
        nint allocPtr = Marshal.AllocHGlobal((nint)inputSizeInBytes);

        try
        {
            // Create OrtValue with the allocated memory as the data buffer.
            using (var ortValueTensor = OrtValue.CreateTensorValueWithData(
                OrtMemoryInfo.DefaultInstance,
                tensorElementType,
                inputShape,
                allocPtr,
                inputSizeInBytes))
            {
                ortIoBinding.BindInput(session.InputNames[0], ortValueTensor);
            }

            // Bind output
            ortIoBinding.BindOutputToDevice(session.OutputNames[0], OrtMemoryInfo.DefaultInstance);

            // Ensure input data is properly synchronized with memory before running the inference.
            ortIoBinding.SynchronizeBoundInputs();

            // Run inference on the OrtIoBinding and bind allocated GPU-memory.
            session.RunWithBinding(runOptions, ortIoBinding);

            // Ensure that the output data is properly synchronized with memory after running the inference.
            ortIoBinding.SynchronizeBoundOutputs();

            // Return the allocated pointer so the caller can free it when done.
            return allocPtr;
        }
        catch
        {
            Marshal.FreeHGlobal(allocPtr);
            throw;
        }
    }
}
