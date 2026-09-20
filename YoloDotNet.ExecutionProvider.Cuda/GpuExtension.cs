// SPDX-License-Identifier: MIT
// SPDX-FileCopyrightText: 2023-2026 Niklas Swärd
// https://github.com/NickSwardh/YoloDotNet

namespace YoloDotNet.ExecutionProvider.Cuda
{
    /// <summary>Provides low-level GPU memory binding helpers for ONNX Runtime.</summary>
    public static class GpuExtension
    {
        /// <summary>
        /// Allocate GPU memory for input data and ensure memory synchronization.
        /// Returns the unmanaged pointer allocated with AllocHGlobal so the caller can free it.
        /// </summary>
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
}
