namespace Snet.Yolo.Tasks.Core.Training;

using System;
using System.Collections.Generic;

/// <summary>Complete training-environment plan.</summary>
/// <param name="EnvReady">Whether the environment is ready.</param>
/// <param name="UseGpu">Whether CUDA is selected.</param>
/// <param name="Device">Ultralytics device argument.</param>
/// <param name="CudaVersion">Selected PyTorch channel or accelerator.</param>
/// <param name="VenvPython">Virtual-environment Python path.</param>
/// <param name="VenvYolo">Virtual-environment YOLO path.</param>
/// <param name="Warning">Optional user-facing warning.</param>
/// <param name="Steps">Ordered setup steps.</param>
public sealed record SetupPlan(
    bool EnvReady,
    bool UseGpu,
    string Device,
    string CudaVersion,
    string VenvPython,
    string VenvYolo,
    string? Warning,
    IReadOnlyList<SetupStep> Steps)
{
    /// <summary>是否具备硬件加速后端（CUDA 或 Apple MPS；macOS 的 MPS 不算 CUDA GPU）。</summary>
    public bool Accelerated => UseGpu || string.Equals(Device, "mps", StringComparison.OrdinalIgnoreCase);
}
