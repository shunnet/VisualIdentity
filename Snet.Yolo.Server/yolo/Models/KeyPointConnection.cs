// SPDX-License-Identifier: MIT
// SPDX-FileCopyrightText: 2025 Niklas Swärd
// https://github.com/NickSwardh/YoloDotNet

using SkiaSharp;
using Snet.Yolo.Server.yolo.Configuration;

namespace Snet.Yolo.Server.yolo.Models
{
    /// <summary>
    /// Represents a connection between pose-markers and their parent marker, defined by index and color.
    /// </summary>
    /// <param name="Index"></param>
    /// <param name="Color"></param>
    public record KeyPointConnection(int Index, string Color);
}
