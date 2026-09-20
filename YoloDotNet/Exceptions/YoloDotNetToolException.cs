// SPDX-License-Identifier: MIT
// SPDX-FileCopyrightText: 2025 Niklas Swärd
// https://github.com/NickSwardh/YoloDotNet

namespace YoloDotNet.Exceptions
{
    /// <summary>Represents invalid tool configuration or an external tool failure.</summary>
    public class YoloDotNetToolException : ArgumentException
    {
        /// <summary>Initializes the exception with an error message.</summary>
        /// <param name="message">The error message.</param>
        public YoloDotNetToolException(string message)
            : base(message)
        {
        }

        /// <summary>Initializes the exception for a named argument.</summary>
        /// <param name="message">The error message.</param>
        /// <param name="paramName">The invalid parameter name.</param>
        public YoloDotNetToolException(string message, string paramName)
            : base(message, paramName)
        {
        }

        /// <summary>Initializes the exception with an underlying cause.</summary>
        /// <param name="message">The error message.</param>
        /// <param name="innerException">The underlying exception.</param>
        public YoloDotNetToolException(string message, Exception innerException)
            : base(message, innerException)
        {
        }
    }
}
