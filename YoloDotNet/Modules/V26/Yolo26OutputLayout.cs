// SPDX-License-Identifier: MIT
// SPDX-FileCopyrightText: 2026 Niklas Swärd
// https://github.com/NickSwardh/YoloDotNet

namespace YoloDotNet.Modules.V26
{
    /// <summary>Identifies which YOLO26 prediction head was exported.</summary>
    internal enum Yolo26OutputLayout
    {
        /// <summary>Channel-first predictions that require external non-maximum suppression.</summary>
        Raw,

        /// <summary>Row-major one-to-one predictions that already contain the selected class.</summary>
        EndToEnd
    }

    /// <summary>Validates and classifies YOLO26 output tensor shapes.</summary>
    internal static class Yolo26OutputLayoutResolver
    {
        /// <summary>Classifies a fixed-attribute output tensor.</summary>
        /// <param name="shape">The output shape in three dimensions.</param>
        /// <param name="rawAttributes">Attribute count for the raw channel-first head.</param>
        /// <param name="endToEndAttributes">Attribute count for the end-to-end row-major head.</param>
        /// <param name="taskName">Task name included in validation errors.</param>
        /// <returns>The exported prediction layout.</returns>
        internal static Yolo26OutputLayout Resolve(
            IReadOnlyList<int> shape,
            int rawAttributes,
            int endToEndAttributes,
            string taskName)
        {
            ArgumentNullException.ThrowIfNull(shape);

            if (shape.Count != 3 || shape[0] != 1 || shape[1] <= 0 || shape[2] <= 0)
                throw new YoloDotNetModelException($"YOLO26 {taskName} output must have a positive [1, rows, columns] shape.");

            if (shape[1] == rawAttributes)
                return Yolo26OutputLayout.Raw;

            if (shape[2] == endToEndAttributes)
                return Yolo26OutputLayout.EndToEnd;

            throw new YoloDotNetModelException(
                $"YOLO26 {taskName} output shape [{string.Join(", ", shape)}] is unsupported. " +
                $"Expected raw attributes {rawAttributes} or end-to-end attributes {endToEndAttributes}.");
        }

        /// <summary>Classifies an output whose task-specific vector count is encoded in the attributes.</summary>
        internal static Yolo26OutputLayout ResolveVariable(
            IReadOnlyList<int> shape,
            int rawBaseAttributes,
            int endToEndBaseAttributes,
            int vectorSize,
            string taskName)
        {
            ArgumentNullException.ThrowIfNull(shape);
            if (shape.Count != 3 || shape[0] != 1 || shape[1] <= 0 || shape[2] <= 0 || vectorSize <= 0)
                throw new YoloDotNetModelException($"YOLO26 {taskName} output must have a positive [1, rows, columns] shape.");

            if (shape[1] > rawBaseAttributes && (shape[1] - rawBaseAttributes) % vectorSize == 0)
                return Yolo26OutputLayout.Raw;

            if (shape[2] > endToEndBaseAttributes && (shape[2] - endToEndBaseAttributes) % vectorSize == 0)
                return Yolo26OutputLayout.EndToEnd;

            throw new YoloDotNetModelException(
                $"YOLO26 {taskName} output shape [{string.Join(", ", shape)}] does not contain valid {vectorSize}-value vectors.");
        }
    }
}
