// SPDX-License-Identifier: MIT
// SPDX-FileCopyrightText: 2023-2025 Niklas Swärd
// https://github.com/NickSwardh/YoloDotNet

namespace YoloDotNet.Models
{
    /// <summary>Stores an intermediate model result before conversion to a public result type.</summary>
    public class ObjectResult
    {
        /// <summary>
        /// Label information associated with the detected object.
        /// </summary>
        public LabelModel Label { get; set; } = new();

        /// <summary>
        /// Confidence score of the detected object.
        /// </summary>
        public double Confidence { get; set; }

        /// <summary>
        /// Region of interest (bounding box) of the detected object.
        /// </summary>
        public SKRectI BoundingBox { get; set; }

        /// <summary>
        /// Region of interest (bounding box) of the detected object for ONNX model dimensions.
        /// </summary>
        public SKRect BoundingBoxUnscaled { get; set; }

        /// <summary>
        /// Index of bounding box
        /// </summary>
        public int BoundingBoxIndex { get; set; }

        /// <summary>
        /// Bit-packed mask where each bit represents a pixel with confidence above a threshold (1 = present, 0 = absent).
        /// </summary>
        public byte[] BitPackedPixelMask { get; set; } = [];

        /// <summary>
        /// Confidence value, X and Y coordinates for Pose Estimation key points
        /// </summary>
        public KeyPoint[] KeyPoints { get; set; } = [];

        /// <summary>
        /// Orientation angle of the bounding box for OBB detections.
        /// </summary>
        public float OrientationAngle { get; set; }

        #region Mapping methods
        /// <summary>Converts an intermediate result to an object-detection result.</summary>
        /// <param name="result">The intermediate result.</param>
        public static explicit operator ObjectDetection(ObjectResult result) => new()
        {
            Label = result.Label,
            Confidence = result.Confidence,
            BoundingBox = result.BoundingBox
        };

        /// <summary>Converts an intermediate result to an oriented-box result.</summary>
        /// <param name="result">The intermediate result.</param>
        public static explicit operator OBBDetection(ObjectResult result) => new()
        {
            Label = result.Label,
            Confidence = result.Confidence,
            BoundingBox = result.BoundingBox,
            OrientationAngle = result.OrientationAngle
        };

        /// <summary>Converts an intermediate result to a segmentation result.</summary>
        /// <param name="result">The intermediate result.</param>
        public static explicit operator Segmentation(ObjectResult result) => new()
        {
            Label = result.Label,
            Confidence = result.Confidence,
            BoundingBox = result.BoundingBox,
            BitPackedPixelMask = result.BitPackedPixelMask
        };

        /// <summary>Converts an intermediate result to a pose-estimation result.</summary>
        /// <param name="result">The intermediate result.</param>
        public static explicit operator PoseEstimation(ObjectResult result) => new()
        {
            Label = result.Label,
            Confidence = result.Confidence,
            BoundingBox = result.BoundingBox,
            KeyPoints = result.KeyPoints
        };
        #endregion
    }
}
