// SPDX-License-Identifier: MIT
// SPDX-FileCopyrightText: 2023-2025 Niklas Swärd
// https://github.com/NickSwardh/YoloDotNet

namespace YoloDotNet.Models.Interfaces
{
    /// <summary>Defines the common data exposed by detection results.</summary>
    public interface IDetection
    {
        /// <summary>Gets the detected class label.</summary>
        LabelModel Label { get; init; }
        /// <summary>Gets the detection confidence.</summary>
        double Confidence { get; init; }
        /// <summary>Gets the axis-aligned bounding box.</summary>
        SKRectI BoundingBox { get; init; }

        // Optional properties used for SORT Tracking
        /// <summary>Gets or sets the optional tracking identifier.</summary>
        int? Id { get; set; }

        /// <summary>Gets or sets the tracked center-point history.</summary>
        List<SKPoint>? Tail { get; set; }
    }
}
