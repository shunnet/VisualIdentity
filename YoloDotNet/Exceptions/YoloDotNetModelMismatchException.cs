// SPDX-License-Identifier: MIT
// SPDX-FileCopyrightText: 2025 Niklas Swärd
// https://github.com/NickSwardh/YoloDotNet

namespace YoloDotNet.Exceptions
{
    /// <summary>Represents a mismatch between a model and the selected processing module.</summary>
    public class YoloDotNetModelMismatchException : Exception
    {
        /// <summary>Initializes the exception with an error message.</summary>
        /// <param name="message">The error message.</param>
        public YoloDotNetModelMismatchException(string message)
            : base(message)
        {
        }

        /// <summary>Initializes the exception with an underlying cause.</summary>
        /// <param name="message">The error message.</param>
        /// <param name="innerException">The underlying exception.</param>
        public YoloDotNetModelMismatchException(string message, Exception innerException)
            : base(message, innerException)
        {
        }
    }
}
