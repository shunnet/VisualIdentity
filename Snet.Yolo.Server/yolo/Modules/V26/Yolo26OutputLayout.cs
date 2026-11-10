// SPDX-License-Identifier: MIT
// SPDX-FileCopyrightText: 2026 Niklas Swärd
// https://github.com/NickSwardh/YoloDotNet

using Snet.Yolo.Server.yolo.Exceptions;

namespace Snet.Yolo.Server.yolo.Modules.V26
{
    /// <summary>Identifies which YOLO26 prediction head was exported.</summary>
    internal enum Yolo26OutputLayout
    {
        /// <summary>Channel-first predictions that require external non-maximum suppression.</summary>
        Raw,

        /// <summary>Row-major one-to-one predictions that already contain the selected class.</summary>
        EndToEnd
    }
}
