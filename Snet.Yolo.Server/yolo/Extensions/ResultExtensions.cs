// SPDX-License-Identifier: MIT
// SPDX-FileCopyrightText: 2026 Niklas Swärd
// https://github.com/NickSwardh/YoloDotNet

#pragma warning disable IL2026 // Reflection-based serialization

using SkiaSharp;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Snet.Yolo.Server.yolo.Models;

namespace Snet.Yolo.Server.yolo.Extensions
{
    /// <summary>Provides JSON serialization helpers for inference results.</summary>
    public static class ResultExtensions
    {
        private static readonly JsonSerializerOptions _jsonOptions = new()
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            Converters = { new SKRectIJsonConverter(), new SKPointJsonConverter(), new SegmentationJsonConverter() }
        };

        private const string CLASSIFICATION_NOT_SUPPORTED =
            "Classification does not use YOLO annotation format. " +
            "YOLO classification datasets use folder-based organization (one folder per class). " +
            "Use ToJson() or SaveJson() to export classification results.";

        #region JSON Export

        /// <summary>
        /// Serializes detection results to a JSON string.
        /// </summary>
        public static string ToJson<T>(this IEnumerable<T> results, JsonSerializerOptions? options = null)
            => JsonSerializer.Serialize(results, options ?? _jsonOptions);

        /// <summary>
        /// Saves detection results to a JSON file.
        /// </summary>
        public static void SaveJson<T>(this IEnumerable<T> results, string filename, JsonSerializerOptions? options = null)
            => File.WriteAllText(filename, results.ToJson(options));

        #endregion

        #region YOLO Format Export - Classification (Not Supported)

        /// <exception cref="NotSupportedException">Classification does not support YOLO annotation format.</exception>
        public static string ToYoloFormat(this IEnumerable<Classification> results, SKBitmap image)
            => throw new NotSupportedException(CLASSIFICATION_NOT_SUPPORTED);

        /// <exception cref="NotSupportedException">Classification does not support YOLO annotation format.</exception>
        public static string ToYoloFormat(this IEnumerable<Classification> results, SKImage image)
            => throw new NotSupportedException(CLASSIFICATION_NOT_SUPPORTED);

        /// <exception cref="NotSupportedException">Classification does not support YOLO annotation format.</exception>
        public static void SaveYoloFormat(this IEnumerable<Classification> results, string filename, SKBitmap image)
            => throw new NotSupportedException(CLASSIFICATION_NOT_SUPPORTED);

        /// <exception cref="NotSupportedException">Classification does not support YOLO annotation format.</exception>
        public static void SaveYoloFormat(this IEnumerable<Classification> results, string filename, SKImage image)
            => throw new NotSupportedException(CLASSIFICATION_NOT_SUPPORTED);

        #endregion

        #region YOLO Format Export - Object Detection

        /// <summary>
        /// Converts object detection results to YOLO annotation format.
        /// <para>Format: <c>class_id x_center y_center width height</c> (normalized 0-1)</para>
        /// </summary>
        public static string ToYoloFormat(this IEnumerable<ObjectDetection> results, SKBitmap image)
            => results.ToYoloFormat(image.Width, image.Height);

        /// <summary>
        /// Converts object detection results to YOLO annotation format.
        /// <para>Format: <c>class_id x_center y_center width height</c> (normalized 0-1)</para>
        /// </summary>
        public static string ToYoloFormat(this IEnumerable<ObjectDetection> results, SKImage image)
            => results.ToYoloFormat(image.Width, image.Height);

        /// <summary>
        /// Saves object detection results to a YOLO annotation file (.txt).
        /// </summary>
        public static void SaveYoloFormat(this IEnumerable<ObjectDetection> results, string filename, SKBitmap image)
            => File.WriteAllText(filename, results.ToYoloFormat(image.Width, image.Height));

        /// <summary>
        /// Saves object detection results to a YOLO annotation file (.txt).
        /// </summary>
        public static void SaveYoloFormat(this IEnumerable<ObjectDetection> results, string filename, SKImage image)
            => File.WriteAllText(filename, results.ToYoloFormat(image.Width, image.Height));

        #endregion

        #region YOLO Format Export - OBB Detection

        /// <summary>
        /// Converts OBB detection results to YOLO OBB annotation format.
        /// <para>Format: <c>class_id x1 y1 x2 y2 x3 y3 x4 y4</c> (4 corner points, normalized 0-1)</para>
        /// </summary>
        public static string ToYoloFormat(this IEnumerable<OBBDetection> results, SKBitmap image)
            => results.ToYoloFormat(image.Width, image.Height);

        /// <summary>
        /// Converts OBB detection results to YOLO OBB annotation format.
        /// <para>Format: <c>class_id x1 y1 x2 y2 x3 y3 x4 y4</c> (4 corner points, normalized 0-1)</para>
        /// </summary>
        public static string ToYoloFormat(this IEnumerable<OBBDetection> results, SKImage image)
            => results.ToYoloFormat(image.Width, image.Height);

        /// <summary>
        /// Saves OBB detection results to a YOLO OBB annotation file (.txt).
        /// </summary>
        public static void SaveYoloFormat(this IEnumerable<OBBDetection> results, string filename, SKBitmap image)
            => File.WriteAllText(filename, results.ToYoloFormat(image.Width, image.Height));

        /// <summary>
        /// Saves OBB detection results to a YOLO OBB annotation file (.txt).
        /// </summary>
        public static void SaveYoloFormat(this IEnumerable<OBBDetection> results, string filename, SKImage image)
            => File.WriteAllText(filename, results.ToYoloFormat(image.Width, image.Height));

        #endregion

        #region YOLO Format Export - Segmentation

        /// <summary>
        /// Converts segmentation results to YOLO segmentation annotation format.
        /// <para>Format: <c>class_id x1 y1 x2 y2 ... xn yn</c> (polygon points, normalized 0-1)</para>
        /// </summary>
        public static string ToYoloFormat(this IEnumerable<Segmentation> results, SKBitmap image)
            => results.ToYoloFormat(image.Width, image.Height);

        /// <summary>
        /// Converts segmentation results to YOLO segmentation annotation format.
        /// <para>Format: <c>class_id x1 y1 x2 y2 ... xn yn</c> (polygon points, normalized 0-1)</para>
        /// </summary>
        public static string ToYoloFormat(this IEnumerable<Segmentation> results, SKImage image)
            => results.ToYoloFormat(image.Width, image.Height);

        /// <summary>
        /// Saves segmentation results to a YOLO segmentation annotation file (.txt).
        /// </summary>
        public static void SaveYoloFormat(this IEnumerable<Segmentation> results, string filename, SKBitmap image)
            => File.WriteAllText(filename, results.ToYoloFormat(image.Width, image.Height));

        /// <summary>
        /// Saves segmentation results to a YOLO segmentation annotation file (.txt).
        /// </summary>
        public static void SaveYoloFormat(this IEnumerable<Segmentation> results, string filename, SKImage image)
            => File.WriteAllText(filename, results.ToYoloFormat(image.Width, image.Height));

        #endregion

        #region YOLO Format Export - Pose Estimation

        /// <summary>
        /// Converts pose estimation results to YOLO pose annotation format.
        /// <para>Format: <c>class_id x_center y_center width height px1 py1 v1 px2 py2 v2 ...</c> (normalized 0-1)</para>
        /// <para>Visibility: 0 = not labeled, 1 = labeled but not visible, 2 = labeled and visible</para>
        /// </summary>
        public static string ToYoloFormat(this IEnumerable<PoseEstimation> results, SKBitmap image, double visibilityThreshold = 0.5)
            => results.ToYoloFormat(image.Width, image.Height, visibilityThreshold);

        /// <summary>
        /// Converts pose estimation results to YOLO pose annotation format.
        /// <para>Format: <c>class_id x_center y_center width height px1 py1 v1 px2 py2 v2 ...</c> (normalized 0-1)</para>
        /// <para>Visibility: 0 = not labeled, 1 = labeled but not visible, 2 = labeled and visible</para>
        /// </summary>
        public static string ToYoloFormat(this IEnumerable<PoseEstimation> results, SKImage image, double visibilityThreshold = 0.5)
            => results.ToYoloFormat(image.Width, image.Height, visibilityThreshold);

        /// <summary>
        /// Saves pose estimation results to a YOLO pose annotation file (.txt).
        /// </summary>
        public static void SaveYoloFormat(this IEnumerable<PoseEstimation> results, string filename, SKBitmap image, double visibilityThreshold = 0.5)
            => File.WriteAllText(filename, results.ToYoloFormat(image.Width, image.Height, visibilityThreshold));

        /// <summary>
        /// Saves pose estimation results to a YOLO pose annotation file (.txt).
        /// </summary>
        public static void SaveYoloFormat(this IEnumerable<PoseEstimation> results, string filename, SKImage image, double visibilityThreshold = 0.5)
            => File.WriteAllText(filename, results.ToYoloFormat(image.Width, image.Height, visibilityThreshold));

        #endregion

        #region Core YOLO Format Methods

        private static string ToYoloFormat(this IEnumerable<ObjectDetection> results, int imageWidth, int imageHeight)
            => BuildYoloFormat(results, imageWidth, imageHeight, (sb, d, w, h) =>
            {
                var (xc, yc, bw, bh) = NormalizeBoundingBox(d.BoundingBox, w, h);
                sb.AppendLine(string.Format(CultureInfo.InvariantCulture, "{0} {1:F6} {2:F6} {3:F6} {4:F6}", d.Label.Index, xc, yc, bw, bh));
            });

        private static string ToYoloFormat(this IEnumerable<OBBDetection> results, int imageWidth, int imageHeight)
            => BuildYoloFormat(results, imageWidth, imageHeight, (sb, d, w, h) =>
            {
                sb.Append(d.Label.Index);
                foreach (var corner in GetRotatedCorners(d.BoundingBox, d.OrientationAngle))
                    sb.Append(string.Format(CultureInfo.InvariantCulture, " {0:F6} {1:F6}", corner.X / w, corner.Y / h));
                sb.AppendLine();
            });

        private static string ToYoloFormat(this IEnumerable<Segmentation> results, int imageWidth, int imageHeight)
            => BuildYoloFormat(results, imageWidth, imageHeight, (sb, d, w, h) =>
            {
                var box = d.BoundingBox;

                // Extract ordered contour points from the bit-packed mask
                var contourPoints = ImageExtension.ExtractOrderedContourPoints(d.BitPackedPixelMask, box.Width, box.Height, maxPoints: 50);

                sb.Append(d.Label.Index);

                if (contourPoints.Count >= 3)
                {
                    foreach (var point in contourPoints)
                    {
                        // Convert from mask-relative to image coordinates, then normalize
                        var px = (box.Left + point.X) / w;
                        var py = (box.Top + point.Y) / h;
                        sb.Append(string.Format(CultureInfo.InvariantCulture, " {0:F6} {1:F6}", px, py));
                    }
                }
                else
                {
                    // Fallback to bounding box corners if contour extraction fails
                    sb.Append(string.Format(CultureInfo.InvariantCulture, " {0:F6} {1:F6}", (float)box.Left / w, (float)box.Top / h));
                    sb.Append(string.Format(CultureInfo.InvariantCulture, " {0:F6} {1:F6}", (float)box.Right / w, (float)box.Top / h));
                    sb.Append(string.Format(CultureInfo.InvariantCulture, " {0:F6} {1:F6}", (float)box.Right / w, (float)box.Bottom / h));
                    sb.Append(string.Format(CultureInfo.InvariantCulture, " {0:F6} {1:F6}", (float)box.Left / w, (float)box.Bottom / h));
                }

                sb.AppendLine();
            });

        private static string ToYoloFormat(this IEnumerable<PoseEstimation> results, int imageWidth, int imageHeight, double visibilityThreshold)
            => BuildYoloFormat(results, imageWidth, imageHeight, (sb, d, w, h) =>
            {
                var (xc, yc, bw, bh) = NormalizeBoundingBox(d.BoundingBox, w, h);
                sb.Append(string.Format(CultureInfo.InvariantCulture, "{0} {1:F6} {2:F6} {3:F6} {4:F6}", d.Label.Index, xc, yc, bw, bh));

                foreach (var kp in d.KeyPoints)
                {
                    var visibility = kp.Confidence >= visibilityThreshold ? 2 : 1;
                    sb.Append(string.Format(CultureInfo.InvariantCulture, " {0:F6} {1:F6} {2}", (float)kp.X / w, (float)kp.Y / h, visibility));
                }
                sb.AppendLine();
            });

        #endregion

        #region Helper Methods

        private static string BuildYoloFormat<T>(IEnumerable<T> results, int width, int height, Action<StringBuilder, T, float, float> formatter)
        {
            var sb = new StringBuilder();
            float w = width, h = height;

            foreach (var item in results)
                formatter(sb, item, w, h);

            return sb.ToString().TrimEnd();
        }

        private static (float XCenter, float YCenter, float Width, float Height) NormalizeBoundingBox(SKRectI rect, float imageWidth, float imageHeight)
            => ((rect.Left + rect.Width / 2f) / imageWidth,
                (rect.Top + rect.Height / 2f) / imageHeight,
                rect.Width / imageWidth,
                rect.Height / imageHeight);

        private static SKPoint[] GetRotatedCorners(SKRectI rect, float angleRadians)
        {
            var cx = rect.MidX;
            var cy = rect.MidY;
            var hw = rect.Width / 2f;
            var hh = rect.Height / 2f;
            var cos = MathF.Cos(angleRadians);
            var sin = MathF.Sin(angleRadians);

            return
            [
                new(cx - hw * cos + hh * sin, cy - hw * sin - hh * cos),
                new(cx + hw * cos + hh * sin, cy + hw * sin - hh * cos),
                new(cx + hw * cos - hh * sin, cy + hw * sin + hh * cos),
                new(cx - hw * cos - hh * sin, cy - hw * sin + hh * cos)
            ];
        }

        #endregion
    }
}
