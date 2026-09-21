namespace Snet.Yolo.Tasks.Services;

using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;

/// <summary>
/// 让 ONNX Runtime 的 CUDA 执行提供程序能找到 CUDA 运行库。
///
/// 背景：Linux 上这些库经常只存在于训练 venv 里（pip 安装的 nvidia-* 包，例如
/// train/.env/lib/python3.13/site-packages/nvidia/cublas/lib/libcublasLt.so.12），
/// 而 ONNX Runtime 只会按系统库搜索路径去找，于是报
/// “Failed to load library libonnxruntime_providers_cuda.so ... libcublasLt.so.12: cannot open shared object file”。
///
/// 做法：在创建推理会话之前，按 SONAME 把候选目录里的 CUDA 库预加载进本进程。
/// glibc 的 dlopen 会优先命中已加载的同名库，因此后续 ONNX Runtime 自己的 dlopen 就能成功。
/// 找不到时返回原因，由调用方降级到 CPU，而不是抛出难懂的原生错误。
/// </summary>
public static class CudaRuntimeLibraries
{
    /// <summary>ORT CUDA EP 必须能解析到的关键库（缺任意一个就无法使用 GPU）。</summary>
    public static readonly IReadOnlyList<string> CriticalLibraries = new[]
    {
        "libcudart.so.12",
        "libcublas.so.12",
        "libcublasLt.so.12",
        "libcufft.so.11",
        "libcudnn.so.9",
    };

    /// <summary>Windows 上 ONNX Runtime CUDA 12/cuDNN 9 的关键 DLL。</summary>
    public static readonly IReadOnlyList<string> WindowsCriticalLibraries = new[]
    {
        "cudart64_12.dll",
        "cublas64_12.dll",
        "cublasLt64_12.dll",
        "cufft64_11.dll",
        "cudnn64_9.dll",
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
        "cudnn64_",
        "cufft64_",
        "curand64_",
        "cusolver64_",
        "cusparse64_",
        "nvrtc64_",
        "nvJitLink_",
    };

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
    /// 候选目录（按优先级）：应用目录 → 训练 venv 内 pip 安装的 nvidia 库目录 → LD_LIBRARY_PATH → 系统 CUDA 目录。
    /// </summary>
    /// <param name="baseDirectory">应用目录（AppContext.BaseDirectory），其下的 train/.env 是训练虚拟环境。</param>
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

        Add(baseDirectory);
        Add(Path.Combine(baseDirectory, "train", "weights"));  // 与权重同放时也能命中
        Add(Path.Combine(baseDirectory, "train", "cuda-runtime"));
        foreach (var directory in PrivateRuntimeLibraryDirectories(baseDirectory)) { Add(directory); }
        foreach (var directory in VirtualEnvironmentLibraryDirectories(baseDirectory)) { Add(directory); }
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

    /// <summary>训练虚拟环境里 pip 安装的 nvidia-* 包自带的库目录。</summary>
    public static IReadOnlyList<string> VirtualEnvironmentLibraryDirectories(string baseDirectory)
    {
        var directories = new List<string>();
        // 同时扫描 Windows 与 Unix 布局，使发布包迁移、交叉平台测试和 Wine/WSL 场景都可预测。
        var windowsSitePackages = Path.Combine(baseDirectory, "train", ".env", "Lib", "site-packages");
        AddPackageLibraryDirectories(windowsSitePackages, directories);
        var torch = Path.Combine(windowsSitePackages, "torch", "lib");
        if (Directory.Exists(torch)) { directories.Add(torch); }
        var venvLibraryRoot = Path.Combine(baseDirectory, "train", ".env", "lib");
        if (!Directory.Exists(venvLibraryRoot)) { return directories; }
        foreach (var pythonDirectory in SafeEnumerateDirectories(venvLibraryRoot, "python*"))
        {
            var nvidiaRoot = Path.Combine(pythonDirectory, "site-packages", "nvidia");
            if (!Directory.Exists(nvidiaRoot)) { continue; }
            foreach (var packageDirectory in SafeEnumerateDirectories(nvidiaRoot, "*"))
            {
                var libraryDirectory = Path.Combine(packageDirectory, "lib");
                if (Directory.Exists(libraryDirectory)) { directories.Add(libraryDirectory); }
            }
        }
        return directories;
    }

    /// <summary>应用自动安装到 train/cuda-runtime 的 NVIDIA wheel 运行库目录。</summary>
    public static IReadOnlyList<string> PrivateRuntimeLibraryDirectories(string baseDirectory)
    {
        var directories = new List<string>();
        AddPackageLibraryDirectories(Path.Combine(baseDirectory, "train", "cuda-runtime"), directories);
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

    /// <summary>判断库文件名是否属于 CUDA 运行库白名单。</summary>
    public static bool IsCudaLibrary(string fileName)
        => CudaLibraryPrefixes.Any(prefix => fileName.StartsWith(prefix, StringComparison.Ordinal));

    /// <summary>
    /// 预加载 CUDA 运行库并校验关键库可用。
    /// 返回 null 表示 GPU 推理可用；否则返回中文原因（调用方据此降级到 CPU）。
    /// 非 Linux（Windows 由系统 PATH 负责）直接返回 null。
    /// </summary>
    /// <param name="baseDirectory">应用目录。</param>
    /// <param name="log">可选的诊断输出。</param>
    public static string? TryPrepare(string baseDirectory, Action<string>? log = null)
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
            if (NativeLibrary.TryLoad(name, out var handle)) { handles.Add(handle); }
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

    private static IEnumerable<string> SafeEnumerateDirectories(string path, string pattern)
    {
        try { return Directory.EnumerateDirectories(path, pattern); }
        catch (IOException) { return Array.Empty<string>(); }
        catch (UnauthorizedAccessException) { return Array.Empty<string>(); }
    }

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
