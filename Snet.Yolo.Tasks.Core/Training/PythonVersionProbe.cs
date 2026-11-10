namespace Snet.Yolo.Tasks.Core.Training;

using System.Collections.Generic;

/// <summary>一条解释器版本探测命令（可执行文件 + 完整参数）。</summary>
public sealed record PythonVersionProbe(PythonLauncher Launcher, IReadOnlyList<string> Arguments);
