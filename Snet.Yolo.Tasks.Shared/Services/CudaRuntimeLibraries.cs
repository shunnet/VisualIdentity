namespace Snet.Yolo.Tasks.Services;

using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;

/// <summary>
/// 让 ONNX Runtime 的 CUDA 执行提供程序能找到 CUDA 运行库。
///
/// 推理使用 CUDA 13，训练使用独立的 PyTorch CUDA 12.8 环境。
/// 私有运行库优先从 train/cuda-runtime/cu13 加载，不扫描训练 venv 或旧 CUDA 12 私有目录。
///
/// 做法：在创建推理会话之前，按 SONAME 把候选目录里的 CUDA 库预加载进本进程。
/// glibc 的 dlopen 会优先命中已加载的同名库，因此后续 ONNX Runtime 自己的 dlopen 就能成功。
/// 找不到时返回原因，由调用方降级到 CPU，而不是抛出难懂的原生错误。
/// </summary>
public static class CudaRuntimeLibraries
{
    private static readonly object PreparationLock = new();
    private static bool _prepared;
    /// <summary>ORT CUDA EP 必须能解析到的关键库（缺任意一个就无法使用 GPU）。</summary>
    public static readonly IReadOnlyList<string> CriticalLibraries = new[]
    {
        "libcudart.so.13",
        "libcublas.so.13",
        "libcublasLt.so.13",
        "libcufft.so.12",
        "libcudnn.so.9",
        "libcudnn_graph.so.9",
    };

    /// <summary>Windows 上 ONNX Runtime CUDA 13/cuDNN 9 的关键 DLL。</summary>
    public static readonly IReadOnlyList<string> WindowsCriticalLibraries = new[]
    {
        "cudart64_13.dll",
        "cublas64_13.dll",
        "cublasLt64_13.dll",
        "cufft64_12.dll",
        "cudnn64_9.dll",
        "cudnn_graph64_9.dll",
    };

    /// <summary>
    /// 允许预加载的库名前缀（白名单）。
    ///
    /// 必须是白名单：候选目录里还包含 LD_LIBRARY_PATH（conda 等）与 /usr/lib/x86_64-linux-gnu，
    /// 若把里面的 .so 全部加载，会连 libasan.so 一起 dlopen —— AddressSanitizer 一旦不是第一个
    /// 加载的库就会直接 abort 整个进程（“ASan runtime does not come first in initial library list”）。
    /// 这里只挑 CUDA 运行库，其余系统库一律不碰。
    /// </summary>
    public static readonly IReadOnlyList<string> CudaLibraryPrefixes = new[]
    {
        "libcudart",
        "libcublas",          // 同时覆盖 libcublasLt
        "libcudnn",           // 同时覆盖 cuDNN 9 的 libcudnn_* 子库
        "libcufft",
        "libcurand",
        "libcusolver",
        "libcusparse",
        "libcusparselt",
        "libnvrtc",
        "libnvjitlink",
        "libnvToolsExt",
        "cudart64_",
        "cublas64_",
        "cublasLt64_",
        "cudnn",              // 包括 Windows 的 cudnn_graph64_9.dll 等子库
        "cufft64_",
        "curand64_",
        "cusolver64_",
        "cusparse64_",
        "nvrtc64_",
        "nvrtc-builtins64_",
        "nvJitLink_",
    };

    private static readonly string[] LinuxCuda13Libraries =
    [
        .. CriticalLibraries, "libcurand.so.10", "libcusolver.so.12", "libcusparse.so.12",
        "libnvrtc.so.13", "libnvrtc-builtins.so.13", "libnvJitLink.so.13",
    ];

    private static readonly string[] WindowsCuda13Libraries =
    [
        .. WindowsCriticalLibraries, "curand64_10.dll", "cusolver64_12.dll", "cusparse64_12.dll", "nvJitLink_130_0.dll",
    ];

    /// <summary>Linux 上常见的 CUDA / 系统库目录。</summary>
    public static readonly IReadOnlyList<string> DefaultSystemDirectories = new[]
    {
        "/usr/local/cuda/lib64",
        "/usr/local/cuda/targets/x86_64-linux/lib",
        "/usr/local/nvidia/lib64",
        "/usr/local/nvidia/lib",
        "/usr/lib/wsl/lib",
        "/usr/lib/x86_64-linux-gnu",
        "/usr/lib64",
    };

    /// <summary>
    /// 候选目录（按优先级）：CUDA 13 私有目录 → 应用目录 → 环境变量路径 → 系统 CUDA 目录。
    /// </summary>
    /// <param name="baseDirectory">应用目录（AppContext.BaseDirectory）。</param>
    /// <param name="libraryPath">LD_LIBRARY_PATH 的值（冒号分隔），可为空。</param>
    /// <param name="systemDirectories">系统目录候选，默认使用 <see cref="DefaultSystemDirectories"/>。</param>
    public static IReadOnlyList<string> CandidateDirectories(
        string baseDirectory,
        string? libraryPath,
        IEnumerable<string>? systemDirectories = null)
    {
        var directories = new List<string>();
        void Add(string? directory)
        {
            if (string.IsNullOrWhiteSpace(directory)) { return; }
            if (directories.Contains(directory, StringComparer.Ordinal)) { return; }
            directories.Add(directory);
        }

        Add(Path.Combine(baseDirectory, "train", "cuda-runtime", "cu13"));
        foreach (var directory in PrivateRuntimeLibraryDirectories(baseDirectory)) { Add(directory); }
        Add(baseDirectory);
        if (!string.IsNullOrEmpty(libraryPath))
        {
            foreach (var directory in libraryPath.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)) { Add(directory); }
        }
        if (OperatingSystem.IsWindows())
        {
            var cudaPath = Environment.GetEnvironmentVariable("CUDA_PATH");
            if (!string.IsNullOrWhiteSpace(cudaPath)) { Add(Path.Combine(cudaPath, "bin")); }
        }
        foreach (var directory in systemDirectories ?? DefaultSystemDirectories) { Add(directory); }
        return directories;
    }

    /// <summary>应用自动安装到 train/cuda-runtime/cu13 的 NVIDIA wheel 运行库目录。</summary>
    public static IReadOnlyList<string> PrivateRuntimeLibraryDirectories(string baseDirectory)
    {
        var directories = new List<string>();
        AddPackageLibraryDirectories(Path.Combine(baseDirectory, "train", "cuda-runtime", "cu13"), directories);
        return directories;
    }

    private static void AddPackageLibraryDirectories(string root, List<string> directories)
    {
        if (!Directory.Exists(root)) { return; }
        foreach (var directory in SafeEnumerateDirectoriesRecursive(root))
        {
            var name = Path.GetFileName(directory);
            if (name.Equals("lib", StringComparison.OrdinalIgnoreCase) || name.Equals("bin", StringComparison.OrdinalIgnoreCase))
            {
                directories.Add(directory);
            }
        }
    }

    /// <summary>
    /// 列出候选目录下**属于 CUDA 运行库**的共享库文件（白名单过滤，绝不加载 libasan/libstdc++ 等系统库）。
    /// </summary>
    public static IReadOnlyList<string> LibraryFiles(IEnumerable<string> directories)
    {
        var files = new List<string>();
        foreach (var directory in directories)
        {
            if (!Directory.Exists(directory)) { continue; }
            foreach (var file in SafeEnumerateFiles(directory))
            {
                var name = Path.GetFileName(file);
                if ((name.Contains(".so", StringComparison.Ordinal) || name.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
                    && IsCudaLibrary(name)) { files.Add(file); }
            }
        }
        return files;
    }

    /// <summary>只接收 CUDA 13 对应的 ABI，避免加载环境变量路径中遗留的 CUDA 12 库。</summary>
    public static bool IsCudaLibrary(string fileName)
    {
        if (!CudaLibraryPrefixes.Any(prefix => fileName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))) { return false; }
        if (fileName.StartsWith("libcudnn", StringComparison.Ordinal))
        {
            var version = fileName.IndexOf(".so.9", StringComparison.Ordinal);
            return version >= 0 && (version + 5 == fileName.Length || fileName[version + 5] == '.');
        }
        if (fileName.StartsWith("cudnn", StringComparison.OrdinalIgnoreCase))
        { return fileName.EndsWith("64_9.dll", StringComparison.OrdinalIgnoreCase); }
        foreach (var soname in LinuxCuda13Libraries)
        {
            if (fileName.Equals(soname, StringComparison.Ordinal) || fileName.StartsWith(soname + ".", StringComparison.Ordinal)) { return true; }
        }
        return WindowsCuda13Libraries.Contains(fileName, StringComparer.OrdinalIgnoreCase)
            || (fileName.StartsWith("nvrtc64_13", StringComparison.OrdinalIgnoreCase) || fileName.StartsWith("nvrtc-builtins64_13", StringComparison.OrdinalIgnoreCase))
                && fileName.EndsWith(".dll", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// 预加载 CUDA 运行库并校验关键库可用。
    /// 返回 null 仅表示运行库检查通过；模型会话仍需成功初始化，失败原因由调用方报告。
    /// Windows 与 Linux 均检查 CUDA 13 ABI，并核对同名 cuDNN 9 是否由 CUDA 13 构建。
    /// </summary>
    /// <param name="baseDirectory">应用目录。</param>
    /// <param name="log">可选的诊断输出。</param>
    public static string? TryPrepare(string baseDirectory, Action<string>? log = null)
    {
        lock (PreparationLock)
        {
            if (_prepared) { return null; }
            var reason = TryPrepareCore(baseDirectory, log);
            _prepared = reason is null;
            return reason;
        }
    }

    /// <summary>在进程级互斥内加载完整运行库；只缓存成功结果，失败可在安装后重试。</summary>
    /// <param name="baseDirectory">应用目录。</param>
    /// <param name="log">诊断回调。</param>
    /// <returns>失败原因；成功为空。</returns>
    private static string? TryPrepareCore(string baseDirectory, Action<string>? log)
    {
        if (OperatingSystem.IsMacOS()) { return "macOS 不支持 NVIDIA CUDA；请使用 CPU 或 Apple MPS。"; }

        var searchPath = OperatingSystem.IsWindows()
            ? Environment.GetEnvironmentVariable("PATH")
            : Environment.GetEnvironmentVariable("LD_LIBRARY_PATH");
        var candidates = CandidateDirectories(baseDirectory, searchPath,
            OperatingSystem.IsWindows() ? Array.Empty<string>() : DefaultSystemDirectories);
        var loaded = new HashSet<string>(StringComparer.Ordinal);
        var handles = new List<IntPtr>();
        var files = LibraryFiles(candidates);

        // 依赖顺序未知，多轮加载直到没有新进展：已在内存中的库会被后续 dlopen 优先命中。
        var progressed = true;
        for (var pass = 0; pass < 4 && progressed; pass++)
        {
            progressed = false;
            foreach (var file in files)
            {
                var name = Path.GetFileName(file);
                if (!loaded.Add(name)) { continue; }
                if (NativeLibrary.TryLoad(file, out var handle)) { handles.Add(handle); progressed = true; }
                else { loaded.Remove(name); }
            }
        }

        var missing = new List<string>();
        var critical = OperatingSystem.IsWindows() ? WindowsCriticalLibraries : CriticalLibraries;
        foreach (var name in critical)
        {
            if (NativeLibrary.TryLoad(name, out var handle))
            {
                handles.Add(handle);
                // cuDNN 入口库会动态加载 graph；依赖缺失时调用入口版本函数可能终止进程。
                // 仅在所有关键库加载成功后，直接查询已加载 graph 库中的版本函数。
                if (name.Contains("cudnn_graph", StringComparison.Ordinal) && missing.Count == 0 && !CudnnUsesCuda13(handle))
                { missing.Add(name + "（需要 CUDA 13 构建）"); }
            }
            else { missing.Add(name); }
        }

        if (missing.Count == 0)
        {
            KeepAlive(handles);
            log?.Invoke($"CUDA 运行库已就绪（候选目录 {candidates.Count} 个，预加载 {loaded.Count} 个库文件）");
            return null;
        }

        // 本轮未形成完整运行时，按加载逆序释放，避免用户多次点击识别时不断增加原生库引用计数；
        // 自动安装完成后的下一轮会从应用私有目录重新加载一套完整且一致的依赖。
        for (var index = handles.Count - 1; index >= 0; index--)
        {
            try { NativeLibrary.Free(handles[index]); } catch { }
        }

        var searched = string.Join("; ", candidates.Where(Directory.Exists));
        return $"缺少 CUDA 运行库 {string.Join("、", missing)}。已搜索目录：{searched}。";
    }

    /// <summary>把已加载的库句柄保持到进程结束，避免被卸载后 ONNX Runtime 又找不到。</summary>
    private static void KeepAlive(List<IntPtr> handles)
    {
        lock (Retained) { Retained.AddRange(handles); }
    }

    private static readonly List<IntPtr> Retained = new();

    /// <summary>cuDNN 9 的 CUDA 12/13 构建共享文件名，必须通过官方 API 区分。</summary>
    private static bool CudnnUsesCuda13(IntPtr handle)
    {
        return NativeLibrary.TryGetExport(handle, "cudnnGetCudartVersion", out var address)
            && Marshal.GetDelegateForFunctionPointer<CudnnCudaVersion>(address)() / 1000 == 13;
    }

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate nuint CudnnCudaVersion();

    private static IEnumerable<string> SafeEnumerateDirectoriesRecursive(string path)
    {
        try { return Directory.EnumerateDirectories(path, "*", SearchOption.AllDirectories); }
        catch (IOException) { return Array.Empty<string>(); }
        catch (UnauthorizedAccessException) { return Array.Empty<string>(); }
    }

    private static IEnumerable<string> SafeEnumerateFiles(string path)
    {
        try { return Directory.EnumerateFiles(path); }
        catch (IOException) { return Array.Empty<string>(); }
        catch (UnauthorizedAccessException) { return Array.Empty<string>(); }
    }
}
