// SPDX-License-Identifier: MIT
// SPDX-FileCopyrightText: 2025 Niklas Swärd
// https://github.com/NickSwardh/YoloDotNet

namespace YoloDotNet.Exceptions
{
    /// <summary>Represents invalid video input or a video-processing failure.</summary>
    public class YoloDotNetVideoException : ArgumentException
    {
        /// <summary>Initializes the exception with an error message.</summary>
        /// <param name="message">The error message.</param>
        public YoloDotNetVideoException(string message)
            : base(message)
        {
        }

        /// <summary>Initializes the exception for a named argument.</summary>
        /// <param name="message">The error message.</param>
        /// <param name="paramName">The invalid parameter name.</param>
        public YoloDotNetVideoException(string message, string paramName)
            : base(message, paramName)
        {
        }

        /// <summary>Initializes the exception with an underlying cause.</summary>
        /// <param name="message">The error message.</param>
        /// <param name="innerException">The underlying exception.</param>
        public YoloDotNetVideoException(string message, Exception innerException)
            : base(message, innerException)
        {
        }
    }
}
