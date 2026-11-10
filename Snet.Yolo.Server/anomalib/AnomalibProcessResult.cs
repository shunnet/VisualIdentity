namespace Snet.Yolo.Server.anomalib;

using System.Text.Json;
using System.Text.Json.Serialization;

/// <summary>Python 流水线进程结果。</summary>
/// <param name="ExitCode">进程退出码。</param>
/// <param name="OutputTail">用于失败诊断的有界输出尾部。</param>
public sealed record AnomalibProcessResult(int ExitCode, string OutputTail);
