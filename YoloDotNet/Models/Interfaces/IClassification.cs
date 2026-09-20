// SPDX-License-Identifier: MIT
// SPDX-FileCopyrightText: 2023-2025 Niklas Swärd
// https://github.com/NickSwardh/YoloDotNet

namespace YoloDotNet.Models.Interfaces
{
    /// <summary>Defines a classification result.</summary>
    public interface IClassification
    {
        /// <summary>Gets or sets the predicted label.</summary>
        string Label { get; set; }
        /// <summary>Gets or sets the prediction confidence.</summary>
        double Confidence { get; set; }
    }
}
