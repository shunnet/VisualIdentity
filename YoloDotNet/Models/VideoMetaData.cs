// SPDX-License-Identifier: MIT
// SPDX-FileCopyrightText: 2025 Niklas Swärd
// https://github.com/NickSwardh/YoloDotNet

namespace YoloDotNet.Models
{
    /// <summary>Describes source and target video dimensions, timing, and device information.</summary>
    /// <param name="Width">Source width in pixels.</param>
    /// <param name="Height">Source height in pixels.</param>
    /// <param name="TargetWidth">Output width in pixels.</param>
    /// <param name="TargetHeight">Output height in pixels.</param>
    /// <param name="Duration">Duration in seconds.</param>
    /// <param name="FPS">Source frames per second.</param>
    /// <param name="TargetFPS">Output frames per second.</param>
    /// <param name="TotalFrames">Estimated source frame count.</param>
    /// <param name="TargetTotalFrames">Estimated output frame count.</param>
    /// <param name="DeviceName">Optional capture-device name.</param>
    public record VideoMetadata(
        int Width,
        int Height,
        int TargetWidth,
        int TargetHeight,
        double Duration,
        double FPS,
        double TargetFPS,
        long TotalFrames,
        long TargetTotalFrames,
        string DeviceName = default!);

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
