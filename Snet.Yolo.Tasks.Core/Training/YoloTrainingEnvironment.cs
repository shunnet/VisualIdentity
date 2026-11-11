namespace Snet.Yolo.Tasks.Core.Training;

using Snet.Yolo.Server.models;

/// <summary>YOLO 训练子进程里与显存有关的环境变量。</summary>
public static class YoloTrainingEnvironment
{
    /// <summary>PyTorch 显存分配器的配置变量名（<c>PYTORCH_ALLOC_CONF</c> 的旧别名，各个 PyTorch 版本都认）。</summary>
    public const string AllocatorConfigName = "PYTORCH_CUDA_ALLOC_CONF";

    /// <summary>
    /// 让分配器按“可扩展的显存段”分配。训练时每批的目标数不同，张量大小不断变化，默认策略会留下许多拼不起来的碎片，
    /// 表现为“显存总量够，却报显存不足”；这是 PyTorch 官方对“已保留但未分配的显存很多”这类 OOM 的建议。
    /// </summary>
    public const string ExpandableSegments = "expandable_segments:True";

    /// <summary>
    /// 计算需要额外设置的显存相关环境变量，不覆盖运行环境里已有的 <see cref="AllocatorConfigName"/>。
    /// 只在单卡的原生 Linux 训练上启用：Windows 的 PyTorch 不支持该选项，WSL2 的 CUDA 虚拟内存接口能力不确定，
    /// 多卡（NCCL）与该实验选项的组合没有验证，这几种情形都保持 PyTorch 默认行为。
    /// </summary>
    /// <param name="device">已经解析好的训练设备，例如 <c>0</c>、<c>0,1</c>、<c>cpu</c>。</param>
    /// <param name="isNativeLinux">是否运行在原生 Linux（不含 WSL）。</param>
    /// <param name="existingAllocatorConfig">运行环境里已有的分配器配置；非空表示管理员自己指定过，原样保留。</param>
    /// <returns>要追加到子进程的环境变量；不需要时为空。</returns>
    public static IReadOnlyDictionary<string, string> MemoryVariables(string device, bool isNativeLinux, string? existingAllocatorConfig)
    {
        if (!isNativeLinux || !string.IsNullOrWhiteSpace(existingAllocatorConfig)) { return new Dictionary<string, string>(); }
        return GpuDeviceSelection.Parse(device, anomalib: false).Length == 1
            ? new Dictionary<string, string> { [AllocatorConfigName] = ExpandableSegments }
            : new Dictionary<string, string>();
    }

    /// <summary>判断当前进程是否运行在 WSL 里：WSL 的内核版本串含 Microsoft。</summary>
    public static bool IsWsl()
    {
        if (!OperatingSystem.IsLinux()) { return false; }
        try { return File.ReadAllText("/proc/sys/kernel/osrelease").Contains("microsoft", StringComparison.OrdinalIgnoreCase); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException) { return false; }
    }
}
