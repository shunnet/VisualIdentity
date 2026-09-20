// SPDX-License-Identifier: MIT
// SPDX-FileCopyrightText: 2023-2025 Niklas Swärd
// https://github.com/NickSwardh/YoloDotNet

namespace YoloDotNet.Models
{
    /// <summary>Contains parsed ONNX inputs, outputs, metadata, and tensor sizing information.</summary>
    /// <param name="Inputs">Model input names and shapes.</param>
    /// <param name="Outputs">Model output names and shapes.</param>
    /// <param name="MetaData">Custom model metadata.</param>
    /// <param name="ModelDataType">The input tensor element type.</param>
    /// <param name="InputShapeSize">The total input element count.</param>
    public record OnnxDataRecord(
        Dictionary<string, long[]> Inputs,
        Dictionary<string, int[]> Outputs,
        Dictionary<string, string> MetaData,
        ModelDataType ModelDataType,
        int InputShapeSize
    );
}
