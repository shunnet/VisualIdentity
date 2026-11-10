// SPDX-License-Identifier: MIT
// SPDX-FileCopyrightText: 2023-2026 Niklas Swärd
// https://github.com/NickSwardh/YoloDotNet

using System.Buffers;
using Snet.Yolo.Server.yolo.Core;
using Snet.Yolo.Server.yolo.Models;
using Snet.Yolo.Server.yolo.Modules.Interfaces;

namespace Snet.Yolo.Server.yolo.Modules.V8
{
    internal sealed class ClassificationEntryComparer : IComparer<ClassificationEntry>
    {
        public static readonly ClassificationEntryComparer Instance = new();
        public int Compare(ClassificationEntry a, ClassificationEntry b) => b.Confidence.CompareTo(a.Confidence);
    }
}
