namespace Snet.Yolo.Tasks.Core.Training;

using System.Runtime.InteropServices;

/// <summary>通过 NVIDIA 驱动随附的 NVML 读取 GPU 核心数和最大图形时钟。</summary>
public static class NvmlGpuPerformance
{
    private const int Success = 0;
    private const int GraphicsClock = 0;

    /// <summary>读取指定 NVIDIA GPU 的性能参数；NVML 不可用或字段不受支持时返回空值。</summary>
    public static (uint? CoreCount, uint? MaxGraphicsClockMhz) TryRead(uint deviceIndex)
    {
        if (!TryLoad(out var library)) { return (null, null); }
        try
        {
            if (!TryGetDelegate(library, "nvmlInit_v2", out NvmlInit initialize)
                || !TryGetDelegate(library, "nvmlShutdown", out NvmlShutdown shutdown)
                || !TryGetDelegate(library, "nvmlDeviceGetHandleByIndex_v2", out NvmlGetDevice getDevice))
            {
                return (null, null);
            }

            if (initialize() != Success) { return (null, null); }
            try
            {
                if (getDevice(deviceIndex, out var device) != Success) { return (null, null); }
                uint? cores = TryGetDelegate(library, "nvmlDeviceGetNumGpuCores", out NvmlGetUInt getCores)
                    && getCores(device, out var coreCount) == Success ? coreCount : null;
                uint? clock = TryGetDelegate(library, "nvmlDeviceGetMaxClockInfo", out NvmlGetClock getClock)
                    && getClock(device, GraphicsClock, out var clockMhz) == Success ? clockMhz : null;
                return (cores, clock);
            }
            finally { _ = shutdown(); }
        }
        catch
        {
            return (null, null);
        }
        finally { NativeLibrary.Free(library); }
    }

    private static bool TryLoad(out nint library)
    {
        var candidates = OperatingSystem.IsWindows()
            ? new[]
            {
                "nvml.dll",
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "System32", "nvml.dll"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "NVIDIA Corporation", "NVSMI", "nvml.dll"),
            }
            : new[] { "libnvidia-ml.so.1", "libnvidia-ml.so", "/usr/lib/wsl/lib/libnvidia-ml.so.1" };
        foreach (var candidate in candidates)
        {
            if (NativeLibrary.TryLoad(candidate, out library)) { return true; }
        }
        library = 0;
        return false;
    }

    private static bool TryGetDelegate<T>(nint library, string name, out T function) where T : Delegate
    {
        if (NativeLibrary.TryGetExport(library, name, out var address))
        {
            function = Marshal.GetDelegateForFunctionPointer<T>(address);
            return true;
        }
        function = null!;
        return false;
    }

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int NvmlInit();
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int NvmlShutdown();
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int NvmlGetDevice(uint index, out nint device);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int NvmlGetUInt(nint device, out uint value);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int NvmlGetClock(nint device, int clockType, out uint clockMhz);
}
