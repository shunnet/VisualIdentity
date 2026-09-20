// SPDX-License-Identifier: MIT
// SPDX-FileCopyrightText: 2025 Niklas Swärd
// https://github.com/NickSwardh/YoloDotNet

namespace YoloDotNet.Trackers
{
    /// <summary>Maintains the state, motion estimate, and tail of one tracked detection.</summary>
    /// <param name="boundingBox">The initial detection.</param>
    /// <param name="tailLength">The maximum number of tail points.</param>
    public class TrackedObject(IDetection boundingBox, int tailLength = 30)
    {
        /// <summary>Gets the latest detection associated with the track.</summary>
        public IDetection BoundingBox { get; private set; } = boundingBox;
        /// <summary>Gets or sets the number of frames since the track was matched.</summary>
        public int Age { get; set; } = 0;
        /// <summary>Gets the motion filter used to predict the next position.</summary>
        public KalmanFilter Kalman { get; private set; } = new KalmanFilter(boundingBox.BoundingBox.MidX, boundingBox.BoundingBox.MidY);

        private readonly TailTrack _tailTracker = new(tailLength);

        /// <summary>Updates the track with a matched detection.</summary>
        /// <param name="detection">The matched detection.</param>
        public void TrackBoundingBox(IDetection detection)
        {
            // Update boundingbox
            BoundingBox = detection;

            // Store current boundingbox center coordinate
            var box = detection.BoundingBox;

            // Update tail
            detection.Tail = _tailTracker.GetTail();

            _tailTracker.AddTailPoint(new SKPointI(box.MidX, box.MidY)); // Store center of boundingbox.

            // Update Kalman filter
            Kalman.Update(box.MidX, box.MidY);
        }

        /// <summary>Advances the motion prediction by one frame.</summary>
        public void KalmanPredict()
            => Kalman.Predict();

        /// <summary>Gets the predicted position and velocity state.</summary>
        /// <returns>An array containing x, y, dx, and dy.</returns>
        public float[] GetPredictedState()
            => Kalman.GetState();
    }
}
