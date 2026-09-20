// SPDX-License-Identifier: MIT
// SPDX-FileCopyrightText: 2025 Niklas Swärd
// https://github.com/NickSwardh/YoloDotNet

namespace YoloDotNet.Extensions
{
    /// <summary>Provides SORT tracking for detection result collections.</summary>
    public static class TrackerExtension
    {
        /// <summary>Updates the tracker and returns detections annotated with tracking information.</summary>
        /// <typeparam name="T">The detection result type.</typeparam>
        /// <param name="detections">The current frame detections.</param>
        /// <param name="sortTrack">The tracker instance to update.</param>
        /// <returns>The supplied detection list after tracking.</returns>
        public static List<T> Track<T>(this List<T> detections, SortTracker sortTrack) where T : IDetection
        {
            sortTrack.UpdateTracker(detections);
            return detections;
        }
    }
}
