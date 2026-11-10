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
    /// <summary>
    /// Custom JSON converter for Segmentation that includes computed contour points.
    /// </summary>
    internal class SegmentationJsonConverter : JsonConverter<Segmentation>
    {
        public override Segmentation Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
            => throw new NotSupportedException("Deserialization of Segmentation is not supported.");

        public override void Write(Utf8JsonWriter writer, Segmentation value, JsonSerializerOptions options)
        {
            writer.WriteStartObject();

            // Label
            writer.WritePropertyName("label");
            writer.WriteStartObject();
            writer.WriteNumber("index", value.Label.Index);
            writer.WriteString("name", value.Label.Name);
            writer.WriteEndObject();

            // Confidence
            writer.WriteNumber("confidence", value.Confidence);

            // BoundingBox
            writer.WritePropertyName("boundingBox");
            writer.WriteStartObject();
            writer.WriteNumber("x", value.BoundingBox.Left);
            writer.WriteNumber("y", value.BoundingBox.Top);
            writer.WriteNumber("width", value.BoundingBox.Width);
            writer.WriteNumber("height", value.BoundingBox.Height);
            writer.WriteEndObject();

            // ContourPoints (computed during serialization)
            if (value.BitPackedPixelMask.Length > 0)
            {
                var box = value.BoundingBox;
                var contourPoints = ImageExtension.ExtractOrderedContourPoints(value.BitPackedPixelMask, box.Width, box.Height);

                if (contourPoints.Count > 0)
                {
                    writer.WritePropertyName("contourPoints");
                    writer.WriteStartArray();

                    foreach (var point in contourPoints)
                    {
                        writer.WriteStartObject();
                        writer.WriteNumber("x", box.Left + point.X);
                        writer.WriteNumber("y", box.Top + point.Y);
                        writer.WriteEndObject();
                    }

                    writer.WriteEndArray();
                }
            }

            writer.WriteEndObject();
        }
    }
}
