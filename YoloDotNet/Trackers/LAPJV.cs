// SPDX-License-Identifier: MIT
// SPDX-FileCopyrightText: 2025-2026 Niklas Swärd
// https://github.com/NickSwardh/YoloDotNet

namespace YoloDotNet.Trackers
{
    /// <summary>
    /// Solves rectangular linear assignment problems in O(n³) time using the
    /// shortest augmenting path form of the Hungarian algorithm.
    /// </summary>
    public static class LAPJV
    {
        /// <summary>Finds the minimum-cost one-to-one assignment for the supplied matrix.</summary>
        /// <param name="costMatrix">Finite costs indexed by row and column.</param>
        /// <returns>One column per row, or -1 when a row cannot be matched.</returns>
        public static int[] Solve(float[,] costMatrix)
        {
            ArgumentNullException.ThrowIfNull(costMatrix);

            var rowCount = costMatrix.GetLength(0);
            var columnCount = costMatrix.GetLength(1);
            if (rowCount == 0 || columnCount == 0)
                throw new YoloDotNetException("Cost matrix must have non-zero dimensions.");

            ValidateFiniteCosts(costMatrix);

            if (rowCount <= columnCount)
                return SolveRowsToColumns(costMatrix);

            var transposed = new float[columnCount, rowCount];
            for (var row = 0; row < rowCount; row++)
                for (var column = 0; column < columnCount; column++)
                    transposed[column, row] = costMatrix[row, column];

            var columnToRow = SolveRowsToColumns(transposed);
            var rowToColumn = Enumerable.Repeat(-1, rowCount).ToArray();
            for (var column = 0; column < columnToRow.Length; column++)
                rowToColumn[columnToRow[column]] = column;

            return rowToColumn;
        }

        /// <summary>Solves a matrix whose row count does not exceed its column count.</summary>
        private static int[] SolveRowsToColumns(float[,] costs)
        {
            var rowCount = costs.GetLength(0);
            var columnCount = costs.GetLength(1);
            var rowPotential = new double[rowCount + 1];
            var columnPotential = new double[columnCount + 1];
            var matchedRow = new int[columnCount + 1];
            var previousColumn = new int[columnCount + 1];

            for (var row = 1; row <= rowCount; row++)
            {
                matchedRow[0] = row;
                var minimum = Enumerable.Repeat(double.PositiveInfinity, columnCount + 1).ToArray();
                var used = new bool[columnCount + 1];
                var currentColumn = 0;

                do
                {
                    used[currentColumn] = true;
                    var currentRow = matchedRow[currentColumn];
                    var delta = double.PositiveInfinity;
                    var nextColumn = 0;

                    for (var column = 1; column <= columnCount; column++)
                    {
                        if (used[column])
                            continue;

                        var reducedCost = costs[currentRow - 1, column - 1]
                            - rowPotential[currentRow]
                            - columnPotential[column];

                        if (reducedCost < minimum[column])
                        {
                            minimum[column] = reducedCost;
                            previousColumn[column] = currentColumn;
                        }

                        if (minimum[column] < delta)
                        {
                            delta = minimum[column];
                            nextColumn = column;
                        }
                    }

                    for (var column = 0; column <= columnCount; column++)
                    {
                        if (used[column])
                        {
                            rowPotential[matchedRow[column]] += delta;
                            columnPotential[column] -= delta;
                        }
                        else
                        {
                            minimum[column] -= delta;
                        }
                    }

                    currentColumn = nextColumn;
                }
                while (matchedRow[currentColumn] != 0);

                do
                {
                    var precedingColumn = previousColumn[currentColumn];
                    matchedRow[currentColumn] = matchedRow[precedingColumn];
                    currentColumn = precedingColumn;
                }
                while (currentColumn != 0);
            }

            var assignment = new int[rowCount];
            for (var column = 1; column <= columnCount; column++)
            {
                if (matchedRow[column] != 0)
                    assignment[matchedRow[column] - 1] = column - 1;
            }

            return assignment;
        }

        /// <summary>Rejects NaN and infinite costs before they enter the optimizer.</summary>
        private static void ValidateFiniteCosts(float[,] costs)
        {
            foreach (var cost in costs)
            {
                if (!float.IsFinite(cost))
                    throw new YoloDotNetException("Cost matrix values must be finite.");
            }
        }
    }
}
