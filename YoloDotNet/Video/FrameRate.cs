// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2025 Niklas Swärd
// https://github.com/NickSwardh/YoloDotNet

namespace YoloDotNet.Video
{
    /// <summary>Represents a supported video frame rate.</summary>
    public readonly struct FrameRate
    {
        /// <summary>Gets the frame rate in frames per second.</summary>
        public float Value { get; }

        private FrameRate(float value)
        {
            Value = value;
        }

        /// <summary>Uses the source or automatically selected frame rate.</summary>
        public static readonly FrameRate AUTO = new(0);
        /// <summary>15 frames per second.</summary>
        public static readonly FrameRate FPS15 = new (15f);
        /// <summary>23.976 frames per second.</summary>
        public static readonly FrameRate FPS23_976 = new (23.976f);
        /// <summary>24 frames per second.</summary>
        public static readonly FrameRate FPS24 = new (24f);
        /// <summary>25 frames per second.</summary>
        public static readonly FrameRate FPS25 = new (25f);
        /// <summary>29.97 frames per second.</summary>
        public static readonly FrameRate FPS29_97 = new (29.97f);
        /// <summary>30 frames per second.</summary>
        public static readonly FrameRate FPS30 = new (30f);
        /// <summary>50 frames per second.</summary>
        public static readonly FrameRate FPS50 = new (50f);
        /// <summary>59.94 frames per second.</summary>
        public static readonly FrameRate FPS59_94 = new (59.94f);
        /// <summary>60 frames per second.</summary>
        public static readonly FrameRate FPS60 = new (60f);

        /// <summary>Converts a frame-rate value to frames per second.</summary>
        /// <param name="rate">The frame-rate value.</param>
        public static implicit operator float(FrameRate rate) => rate.Value;
    }
}
