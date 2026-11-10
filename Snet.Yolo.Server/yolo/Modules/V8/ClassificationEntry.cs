// SPDX-License-Identifier: MIT
// SPDX-FileCopyrightText: 2023-2026 Niklas Swärd
// https://github.com/NickSwardh/YoloDotNet

using System.Buffers;
using Snet.Yolo.Server.yolo.Core;
using Snet.Yolo.Server.yolo.Models;
using Snet.Yolo.Server.yolo.Modules.Interfaces;

namespace Snet.Yolo.Server.yolo.Modules.V8
{
    internal readonly struct ClassificationEntry(float confidence, int labelId)
    {
        public readonly float Confidence = confidence;  // confidence score
        public readonly int LabelId = labelId;          // index into labels array
    }
}
