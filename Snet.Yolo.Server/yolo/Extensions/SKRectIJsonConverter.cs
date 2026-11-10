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
    /// Custom JSON converter for SKRectI to avoid infinite recursion from Standardized property.
    /// </summary>
    internal class SKRectIJsonConverter : JsonConverter<SKRectI>
    {
        public override SKRectI Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType != JsonTokenType.StartObject)
                throw new JsonException();

            int x = 0, y = 0, width = 0, height = 0;

            while (reader.Read())
            {
                if (reader.TokenType == JsonTokenType.EndObject)
                    break;

                if (reader.TokenType == JsonTokenType.PropertyName)
                {
                    var propertyName = reader.GetString();
                    reader.Read();

                    switch (propertyName?.ToLowerInvariant())
                    {
                        case "x": x = reader.GetInt32(); break;
                        case "y": y = reader.GetInt32(); break;
                        case "width": width = reader.GetInt32(); break;
                        case "height": height = reader.GetInt32(); break;
                    }
                }
            }

            return SKRectI.Create(x, y, width, height);
        }

        public override void Write(Utf8JsonWriter writer, SKRectI value, JsonSerializerOptions options)
        {
            writer.WriteStartObject();
            writer.WriteNumber("x", value.Left);
            writer.WriteNumber("y", value.Top);
            writer.WriteNumber("width", value.Width);
            writer.WriteNumber("height", value.Height);
            writer.WriteEndObject();
        }
    }
}
