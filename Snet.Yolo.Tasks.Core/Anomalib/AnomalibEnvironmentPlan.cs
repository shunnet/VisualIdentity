namespace Snet.Yolo.Tasks.Core.Anomalib;

using Snet.Yolo.Tasks.Core.Training;

/// <summary>Anomalib 独立虚拟环境的安装计划。</summary>
public sealed class AnomalibEnvironmentPlan
{
    /// <summary>独立虚拟环境目录。</summary>
    public required string VenvDirectory { get; init; }

    /// <summary>虚拟环境内的 Python 可执行文件。</summary>
    public required string VenvPython { get; init; }

    /// <summary>按顺序执行的环境搭建步骤。</summary>
    public required IReadOnlyList<SetupStep> Steps { get; init; }
}
