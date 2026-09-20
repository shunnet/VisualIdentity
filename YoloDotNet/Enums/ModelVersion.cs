// SPDX-License-Identifier: MIT
// SPDX-FileCopyrightText: 2023-2026 Niklas Swärd
// https://github.com/NickSwardh/YoloDotNet

namespace YoloDotNet.Enums
{
    /// <summary>
    /// Strongly typed Yolo model versions
    /// </summary>
    public enum ModelVersion
    {
        /// <summary>YOLOv5u.</summary>
        V5U,
        /// <summary>YOLOv8.</summary>
        V8,
        /// <summary>YOLOv8 end-to-end.</summary>
        V8E,
        /// <summary>YOLOv9.</summary>
        V9,
        /// <summary>YOLOv10.</summary>
        V10,
        /// <summary>YOLOv11.</summary>
        V11,
        /// <summary>YOLOv11 end-to-end.</summary>
        V11E,
        /// <summary>YOLOv12.</summary>
        V12,
        /// <summary>YOLOv26.</summary>
        V26,
        /// <summary>YOLOE built on the YOLO26 architecture.</summary>
        V26E,
        /// <summary>Real-Time Detection Transformer.</summary>
        RTDETR,
        /// <summary>YOLO-World v2.</summary>
        WORLDV2
    }
}
