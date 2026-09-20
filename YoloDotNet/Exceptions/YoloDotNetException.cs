// SPDX-License-Identifier: MIT
// SPDX-FileCopyrightText: 2025 Niklas Swärd
// https://github.com/NickSwardh/YoloDotNet

namespace YoloDotNet.Exceptions
{
    /// <summary>Represents an invalid argument supplied to YoloDotNet.</summary>
    public class YoloDotNetException : ArgumentException
    {
        /// <summary>Initializes the exception with an error message.</summary>
        /// <param name="message">The error message.</param>
        public YoloDotNetException(string message)
            : base(message)
        {
        }

        /// <summary>Initializes the exception for a named argument.</summary>
        /// <param name="message">The error message.</param>
        /// <param name="paramName">The invalid parameter name.</param>
        public YoloDotNetException(string message, string paramName)
            : base(message, paramName)
        {
        }

        /// <summary>Initializes the exception with an underlying cause.</summary>
        /// <param name="message">The error message.</param>
        /// <param name="innerException">The underlying exception.</param>
        public YoloDotNetException(string message, Exception innerException)
            : base(message, innerException)
        {
        }
    }
}
