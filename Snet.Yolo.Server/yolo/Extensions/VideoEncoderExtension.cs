// SPDX-License-Identifier: MIT
// SPDX-FileCopyrightText: 2025 Niklas Swärd
// https://github.com/NickSwardh/YoloDotNet

using Snet.Yolo.Server.yolo.Attributes;
using Snet.Yolo.Server.yolo.Enums;
namespace Snet.Yolo.Server.yolo.Extensions
{
    internal static class VideoEncoderExtensions
    {
        /// <summary>
        /// Gets the encoder name associated with the specified Encoder enum value.
        /// </summary>
        /// <param name="encoder"></param>
        public static string GetEncoderName(this VideoEncoder encoder)
        {
            var field = encoder.GetType().GetField(encoder.ToString());
            var attr = (EncoderNameAttribute?)Attribute.GetCustomAttribute(field!, typeof(EncoderNameAttribute));
            return attr?.Name ?? encoder.ToString().ToLowerInvariant();
        }

        public static bool IsLocalFile(this string videoInput)
            => File.Exists(videoInput);
    }
}
