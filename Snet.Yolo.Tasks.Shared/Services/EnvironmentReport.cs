namespace Snet.Yolo.Tasks.Services;

/// <summary>某个组件的环境自检报告。</summary>
/// <param name="Component">被检查的组件。</param>
/// <param name="Items">逐项检查结果，按展示顺序排列。</param>
/// <param name="HostHasGpu">宿主机是否检测到 NVIDIA GPU。</param>
/// <param name="CheckedAt">自检完成时间（UTC）。</param>
public sealed record EnvironmentReport(EnvironmentComponent Component, IReadOnlyList<EnvironmentCheckItem> Items, bool HostHasGpu, DateTime CheckedAt)
{
    /// <summary>所有要求项都通过时环境可用。</summary>
    public bool Ready => Items.All(static item => item.Ok || !item.Required);

    /// <summary>宿主机有 GPU 时，GPU 相关的要求项是否都通过（非要求项如纯 CPU 发行包的 GPU 运行库不计入）；没有 GPU 时视为无需检查。</summary>
    public bool GpuReady => !HostHasGpu || Items.Where(static item => item.Key.StartsWith("Gpu", StringComparison.Ordinal)).All(static item => item.Ok || !item.Required);

    /// <summary>是否建议重置：环境不可用，或宿主机有 GPU 却没有可用的 GPU 运行库。</summary>
    public bool NeedsRepair => !Ready || !GpuReady;
}
