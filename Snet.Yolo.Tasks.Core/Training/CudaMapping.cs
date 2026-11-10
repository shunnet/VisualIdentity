namespace Snet.Yolo.Tasks.Core.Training;

using System;

/// <summary>
/// GPU 计算能力和驱动版本到 PyTorch CUDA wheel 的映射。
/// CUDA 11.8 保留给 Pascal/Volta/Turing，Ampere 及更新架构优先使用与 ONNX Runtime
/// PyTorch 的 CUDA 12.8 训练通道；与 .NET 推理使用的 CUDA 13 运行库分别管理。
/// </summary>
public static class CudaMapping
{
    /// <summary>无法判定计算能力时选择兼容面更广的 CUDA 11.8 通道。</summary>
    public const string ConservativeChannel = "cu118";

    /// <summary>CUDA 11.x minor compatibility 所需的最低 Linux 驱动主版本。</summary>
    public const int Cuda11MinimumLinuxDriver = 450;

    /// <summary>CUDA 11.x minor compatibility 所需的最低 Windows 驱动主版本。</summary>
    public const int Cuda11MinimumWindowsDriver = 452;

    /// <summary>CUDA 12.x minor compatibility 所需的最低 Linux 驱动主版本。</summary>
    public const int Cuda12MinimumLinuxDriver = 525;

    /// <summary>CUDA 12.x minor compatibility 所需的最低 Windows 驱动主版本。</summary>
    public const int Cuda12MinimumWindowsDriver = 527;

    /// <summary>Maps a CUDA compute capability to a compatible PyTorch wheel channel.</summary>
    public static string Map(double? computeCap)
    {
        if (computeCap is null) { return "cpu"; }
        return computeCap switch
        {
            < 6.0 => "cpu",
            < 7.0 => "cu118",
            < 8.0 => "cu118",
            _ => "cu128",
        };
    }

    /// <summary>
    /// 同时考虑计算能力和当前驱动选择 wheel 通道。低于计算能力 6.0 的旧卡不再安装
    /// 已停止维护的 CUDA 10.2/PyTorch 旧包，直接使用现代 CPU 构建。
    /// </summary>
    public static string Select(double? computeCap, string? driverVersion, OsKind os)
    {
        if (computeCap is null) { return DriverSupports(ConservativeChannel, driverVersion, os) ? ConservativeChannel : "cpu"; }
        var preferred = Map(computeCap);
        if (preferred == "cpu") { return preferred; }
        if (DriverSupports(preferred, driverVersion, os)) { return preferred; }
        return computeCap < 10 && DriverSupports("cu118", driverVersion, os) ? "cu118" : "cpu";
    }

    /// <summary>判断 NVIDIA 驱动是否满足指定 CUDA 主版本的最低要求。</summary>
    public static bool DriverSupports(string channel, string? driverVersion, OsKind os)
    {
        if (channel.StartsWith("cu13", StringComparison.Ordinal))
        {
            var minimumVersion = os == OsKind.Windows ? new Version(580, 88) : new Version(580, 65, 6);
            return Version.TryParse(driverVersion?.Trim(), out var driver) && driver >= minimumVersion;
        }
        var major = DriverMajor(driverVersion);
        if (major is null) { return true; }
        var minimum = channel.StartsWith("cu12", StringComparison.Ordinal)
            ? (os == OsKind.Windows ? Cuda12MinimumWindowsDriver : Cuda12MinimumLinuxDriver)
            : (os == OsKind.Windows ? Cuda11MinimumWindowsDriver : Cuda11MinimumLinuxDriver);
        return major >= minimum;
    }

    /// <summary>检查 torch 的 CUDA 主版本及最低次版本，避免新显卡复用不支持其架构的旧 wheel。</summary>
    public static bool RuntimeMatches(string channel, string? runtimeVersion)
    {
        if (channel == "cpu") { return true; }
        if (!Version.TryParse(runtimeVersion, out var runtime)) { return false; }
        var minimum = channel switch
        {
            "cu118" => new Version(11, 8),
            "cu126" => new Version(12, 6),
            "cu128" => new Version(12, 8),
            _ => null,
        };
        return minimum is not null && runtime.Major == minimum.Major && runtime >= minimum;
    }

    /// <summary>解析形如 535.104.05 或 551.86 的驱动主版本。</summary>
    public static int? DriverMajor(string? driverVersion)
    {
        if (string.IsNullOrWhiteSpace(driverVersion)) { return null; }
        var first = driverVersion.Trim().Split('.')[0];
        return int.TryParse(first, out var value) ? value : null;
    }

    /// <summary>已知存在 GPU 但无法确定计算能力时的保守通道（不要静默降级为 CPU）。</summary>
    public static string MapUnknown() => ConservativeChannel;

    /// <summary>常见 compute_cap 可能形如 8.6 / 6.1，返回有效数值。</summary>
    public static double? TryParse(string? computeCap)
    {
        if (string.IsNullOrWhiteSpace(computeCap)) { return null; }
        var s = computeCap.Trim().Replace(",", ".");
        return double.TryParse(s, out var v) ? v : null;
    }
}
