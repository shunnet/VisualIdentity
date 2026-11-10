// SPDX-License-Identifier: MIT
// SPDX-FileCopyrightText: 2025 Niklas Swärd
// https://github.com/NickSwardh/YoloDotNet

using System.Text.Json.Serialization;

namespace Snet.Yolo.Server.yolo.Models
{
    internal class Metadata
    {
        [JsonPropertyName("width")]
        public int Width { get; set; }

        [JsonPropertyName("height")]
        public int Height { get; set; }

        [JsonPropertyName("frameratenumerator")]
        public int FrameRateNumerator { get; set; }

        [JsonPropertyName("frameratedenominator")]
        public int FrameRateDenominator { get; set; }

        [JsonPropertyName("duration")]
        public double Duration { get; set; }

        public double FPS => (double)FrameRateNumerator / FrameRateDenominator;

        public long TotalFrames => ((int)Math.Floor(FPS * Duration)) - 1; // Set -1 to keep total frames zero-index.
    }
}
