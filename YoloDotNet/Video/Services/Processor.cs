// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2025 Niklas Swärd
// https://github.com/NickSwardh/YoloDotNet

namespace YoloDotNet.Video.Services
{
    /// <summary>Creates safely configured external tool processes.</summary>
    public class Processor
    {
        /// <summary>Creates a process with redirected streams and an argument list.</summary>
        /// <param name="fileName">The executable path or name.</param>
        /// <param name="arguments">Arguments passed without shell interpolation.</param>
        /// <returns>The configured, unstarted process.</returns>
        public static Process Create(string fileName, IEnumerable<string> arguments)
        { 
            var process = new Process()
               {
                   StartInfo = new ProcessStartInfo
                   {
                       FileName = fileName,
                       UseShellExecute = false,
                       RedirectStandardOutput = true,
                       RedirectStandardError = true,
                       RedirectStandardInput = true,
                       CreateNoWindow = true
                   },

                   EnableRaisingEvents = true,
               };

            // Add each argument safely to ArgumentList
            foreach (var arg in arguments)
                process.StartInfo.ArgumentList.Add(arg);

            return process;

        }
    }
}
