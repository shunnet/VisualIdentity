// SPDX-License-Identifier: MIT
// SPDX-FileCopyrightText: 2025-2026 Niklas Swärd
// https://github.com/NickSwardh/YoloDotNet

namespace YoloDotNet.Models.Interfaces
{
    /// <summary>
    /// Interface for execution providers to implement for running inference on ONNX models.
    /// </summary>
    public interface IExecutionProvider
    {
        /// <summary>
        /// Method to run inference on the model with the provided normalized pixel data.
        /// </summary>
        /// <typeparam name="T">The model input element type: <see cref="float"/> or <see cref="ushort"/> for Float16 bit patterns.</typeparam>
        /// <param name="normalizedPixels">The normalized tensor data. The array must contain at least the model input element count.</param>
        /// <returns>An inference result that must be disposed after its output spans have been consumed.</returns>
        public InferenceResult Run<T>(T[] normalizedPixels) where T : unmanaged;

        /// <summary>
        /// Record containing metadata about the ONNX model.
        /// </summary>
        public OnnxModel OnnxData { get; }

        /// <summary>
        /// Gets the current session associated with the context.
        /// </summary>
        public object Session { get; }

        /// <summary>
        /// Releases all resources used by the current instance of the class.
        /// </summary>
        public void Dispose();
    }
}
