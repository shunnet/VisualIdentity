// SPDX-License-Identifier: MIT
// SPDX-FileCopyrightText: 2023-2025 Niklas Swärd
// https://github.com/NickSwardh/YoloDotNet

namespace YoloDotNet.Modules.Interfaces
{
    /// <summary>Defines a disposable model-processing module.</summary>
    public interface IModule : IDisposable
    {
        /// <summary>Gets the parsed metadata for the loaded ONNX model.</summary>
        OnnxModel OnnxModel { get; }
    }
}
