// SPDX-License-Identifier: MIT
// SPDX-FileCopyrightText: 2025 Niklas Swärd
// https://github.com/NickSwardh/YoloDotNet

namespace YoloDotNet.Exceptions
{
    /// <summary>Represents invalid or unsupported ONNX model data.</summary>
    public class YoloDotNetModelException : ArgumentException
    {
        /// <summary>Initializes the exception with an error message.</summary>
        /// <param name="message">The error message.</param>
        public YoloDotNetModelException(string message)
            : base(message)
        {
        }

        /// <summary>Initializes the exception for a named argument.</summary>
        /// <param name="message">The error message.</param>
        /// <param name="paramName">The invalid parameter name.</param>
        public YoloDotNetModelException(string message, string paramName)
            : base(message, paramName)
        {
        }

        /// <summary>Initializes the exception with an underlying cause.</summary>
        /// <param name="message">The error message.</param>
        /// <param name="innerException">The underlying exception.</param>
        public YoloDotNetModelException(string message, Exception innerException)
            : base(message, innerException)
        {
        }
    }
}
