// SPDX-License-Identifier: MIT
// SPDX-FileCopyrightText: 2025 Niklas Swärd
// https://github.com/NickSwardh/YoloDotNet

using SkiaSharp;
using Snet.Yolo.Server.yolo.Configuration;

namespace Snet.Yolo.Server.yolo.Models
{
    /// <summary>Configures drawing of pose-estimation results.</summary>
    public class PoseDrawingOptions : CommonBoundingBoxOptions
    {
        /// <summary>
        /// Confidence threshold for displaying keypoints.
        /// </summary>
        public double PoseConfidence { get; set; } = ImageConfig.POSE_KEYPOINT_THRESHOLD;

        /// <summary>
        /// Default color used to draw keypoints when no specific colors or markers are provided via <see cref="KeyPointMarkers"/>.
        /// </summary>
        public SKColor DefaultPoseColor { get; set; } = ImageConfig.PoseMarkerColor;

        /// <summary>
        /// User-defined mapping to determine how to connect keypoints and specify associated colors.
        /// </summary>
        public KeyPointMarker[] KeyPointMarkers { get; set; } = [];

        #region Mapping method
        /// <summary>Copies common pose options into detection drawing options.</summary>
        /// <param name="options">The pose drawing options.</param>
        public static explicit operator DetectionDrawingOptions(PoseDrawingOptions options) => new()
        {
            Font = options.Font,
            FontColor = options.FontColor,
            FontSize = options.FontSize,
            EnableFontShadow = options.EnableFontShadow,
            EnableDynamicScaling = options.EnableDynamicScaling,
            DrawLabelBackground = options.DrawLabelBackground,
            DrawBoundingBoxes = options.DrawBoundingBoxes,
            DrawLabels = options.DrawLabels,
            DrawConfidenceScore = options.DrawConfidenceScore,
            DrawTrackedTail = options.DrawTrackedTail,
            BorderThickness = options.BorderThickness,
            BoundingBoxOpacity = options.BoundingBoxOpacity,
            BoundingBoxHexColors = options.BoundingBoxHexColors,
            TailThickness = options.TailThickness,
            TailPaintColorStart = options.TailPaintColorStart,
            TailPaintColorEnd = options.TailPaintColorEnd,
            LabelPosition = options.LabelPosition,
        };
        #endregion
    }
}
