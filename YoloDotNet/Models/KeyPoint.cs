// SPDX-License-Identifier: MIT
// SPDX-FileCopyrightText: 2023-2025 Niklas Swärd
// https://github.com/NickSwardh/YoloDotNet

namespace YoloDotNet.Models
{
    /// <summary>Represents a predicted key point.</summary>
    /// <param name="X">The horizontal image coordinate.</param>
    /// <param name="Y">The vertical image coordinate.</param>
    /// <param name="Confidence">The key-point confidence.</param>
    public record KeyPoint(int X, int Y, double Confidence);
}
