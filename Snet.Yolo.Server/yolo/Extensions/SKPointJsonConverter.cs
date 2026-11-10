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
    /// Custom JSON converter for SKPoint to provide clean x/y output.
    /// </summary>
    internal class SKPointJsonConverter : JsonConverter<SKPoint>
    {
        public override SKPoint Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType != JsonTokenType.StartObject)
                throw new JsonException();

            float x = 0, y = 0;

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
                        case "x": x = reader.GetSingle(); break;
                        case "y": y = reader.GetSingle(); break;
                    }
                }
            }

            return new SKPoint(x, y);
        }

        public override void Write(Utf8JsonWriter writer, SKPoint value, JsonSerializerOptions options)
        {
            writer.WriteStartObject();
            writer.WriteNumber("x", value.X);
            writer.WriteNumber("y", value.Y);
            writer.WriteEndObject();
        }
    }
}
