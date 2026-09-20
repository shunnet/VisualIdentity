// SPDX-License-Identifier: MIT
// SPDX-FileCopyrightText: 2023-2025 Niklas Swärd
// https://github.com/NickSwardh/YoloDotNet

namespace YoloDotNet.Enums
{
    /// <summary>
    /// Strongly typed names for ONNX metadata properties.
    /// </summary>
    public enum MetaData
    {
        /// <summary>The model task metadata key.</summary>
        Task,
        /// <summary>The label-name metadata key.</summary>
        Names,
        /// <summary>The model description metadata key.</summary>
        Description
    }
}
