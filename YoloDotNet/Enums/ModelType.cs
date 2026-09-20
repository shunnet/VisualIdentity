// SPDX-License-Identifier: MIT
// SPDX-FileCopyrightText: 2023-2025 Niklas Swärd
// https://github.com/NickSwardh/YoloDotNet

namespace YoloDotNet.Enums
{
    /// <summary>
    /// Strongly typed names for image vision types.
    /// </summary>
    [DataContract]
    public enum ModelType
    {
        /// <summary>Image classification.</summary>
        [EnumMember(Value = "classify")]
        Classification,

        /// <summary>Axis-aligned object detection.</summary>
        [EnumMember(Value = "detect")]
        ObjectDetection,

        /// <summary>Oriented bounding-box detection.</summary>
        [EnumMember(Value = "obb")]
        ObbDetection,

        /// <summary>Instance segmentation.</summary>
        [EnumMember(Value = "segment")]
        Segmentation,

        /// <summary>Human or object pose estimation.</summary>
        [EnumMember(Value = "pose")]
        PoseEstimation,

        /// <summary>Dense per-pixel semantic segmentation.</summary>
        [EnumMember(Value = "semantic")]
        SemanticSegmentation,

        /// <summary>Dense monocular depth estimation.</summary>
        [EnumMember(Value = "depth")]
        DepthEstimation
    }
}
