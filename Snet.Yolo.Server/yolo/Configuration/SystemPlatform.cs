// SPDX-License-Identifier: MIT
// SPDX-FileCopyrightText: 2025 Niklas Swärd
// https://github.com/NickSwardh/YoloDotNet

using System.Runtime.InteropServices;
using Snet.Yolo.Server.yolo.Enums;
namespace Snet.Yolo.Server.yolo.Configuration
{
    internal static class SystemPlatform
    {
        /// <summary>
        /// Detects the current operating system platform.
        /// </summary>
        /// <returns>An OSPlatformType enum indicating the detected OS.</returns>
        public static Platform GetOS()
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
                return Platform.Linux;

            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                return Platform.Windows;

            if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
                return Platform.MacOS;

            return Platform.Unknown;
        }
    }
}
