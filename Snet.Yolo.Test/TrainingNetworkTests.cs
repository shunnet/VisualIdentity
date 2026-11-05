using Microsoft.Extensions.Configuration;
using Snet.Yolo.Tasks.Core.Training;
using Snet.Yolo.Tasks.Services;
using Xunit;

namespace Snet.Yolo.Test;

/// <summary>
/// 企业网络下的训练联网问题：自定义 CA（HTTPS 被代理拦截）与预训练权重缓存。
/// 现场表现：pip 装得上，但 Ultralytics 下载 yolo*.pt 时报 curl 60 / certificate verify failed，
/// 训练直接终止。这里把“怎么把 CA 传下去”和“怎么复用本地权重”固定成契约。
/// </summary>
public sealed class TrainingNetworkTests
{
    [Fact]
    public void WeightSettings_UseUltralyticsInterfaceAndPassCachePathAsSeparateArgument()
    {
        var path = Path.Combine(Path.GetTempPath(), "weights with spaces ' 中文");
        var arguments = YoloCommandBuilder.BuildWeightSettingsArguments(path);
        Assert.Equal(3, arguments.Count);
        Assert.Equal("-c", arguments[0]);
        Assert.Contains("settings.update(weights_dir=sys.argv[1])", arguments[1]);
        Assert.Equal(Path.GetFullPath(path), arguments[2]);
        Assert.DoesNotContain(path, arguments[1]);
        Assert.Throws<ArgumentException>(() => YoloCommandBuilder.BuildWeightSettingsArguments(""));
    }

    [Fact]
    public void EnvironmentOverrides_FirstAttemptWithoutCaBundle_InheritsProcessEnvironment()
    {
        Assert.Null(PipProxyPolicy.BuildEnvironmentOverrides(1));
    }

    [Fact]
    public void EnvironmentOverrides_FirstAttemptWithCaBundle_OnlyAddsCertificateVariables()
    {
        var caBundle = "/etc/ssl/certs/proxy-ca.crt";

        var overrides = PipProxyPolicy.BuildEnvironmentOverrides(1, caBundle);

        Assert.NotNull(overrides);
        foreach (var name in PipProxyPolicy.CertificateVariables) { Assert.Equal(caBundle, overrides![name]); }
        // 第 1 次尝试不能动代理设置，否则会把用户的可用代理也一起关掉。
        foreach (var name in PipProxyPolicy.ProxyVariables) { Assert.False(overrides!.ContainsKey(name)); }
    }

    [Fact]
    public void EnvironmentOverrides_RetryWithCaBundle_DisablesProxyAndKeepsCaBundle()
    {
        var caBundle = "/etc/ssl/certs/proxy-ca.crt";

        var overrides = PipProxyPolicy.BuildEnvironmentOverrides(2, caBundle);

        Assert.NotNull(overrides);
        foreach (var name in PipProxyPolicy.ProxyVariables) { Assert.Null(overrides![name]); }
        foreach (var name in PipProxyPolicy.NoProxyVariables) { Assert.Equal(PipProxyPolicy.NoProxyValue, overrides![name]); }
        foreach (var name in PipProxyPolicy.CertificateVariables) { Assert.Equal(caBundle, overrides![name]); }
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void EnvironmentOverrides_BlankCaBundle_IsIgnored(string caBundle)
    {
        Assert.Null(PipProxyPolicy.BuildEnvironmentOverrides(1, caBundle));
    }

    [Fact]
    public void CaBundleConfiguration_IsTrimmedAndBlankBecomesNull()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Training:CaBundle"] = "  /etc/ssl/certs/proxy-ca.crt  ",
                ["Training:Proxy"] = "   ",
            })
            .Build();

        Assert.Equal("/etc/ssl/certs/proxy-ca.crt", TrainingService.ReadCaBundle(configuration));
        Assert.Null(TrainingService.ReadProxy(configuration));
        Assert.Null(TrainingService.ReadCaBundle(null));
    }

    [Fact]
    public void WeightCandidates_PreferWorkingDirectoryThenAppCacheThenUltralyticsDirectory()
    {
        var candidates = WeightCache.Candidates(@"C:\app\train\yolo\weights", @"C:\app\train\yolo\snet\p1", "yolo26n.pt");

        Assert.Equal(3, candidates.Count);
        Assert.Equal(Path.Combine(@"C:\app\train\yolo\snet\p1", "yolo26n.pt"), candidates[0]);
        Assert.Equal(Path.Combine(@"C:\app\train\yolo\weights", "yolo26n.pt"), candidates[1]);
        Assert.Equal(Path.Combine(WeightCache.UltralyticsWeightsDirectory(), "yolo26n.pt"), candidates[2]);
    }

    [Fact]
    public void UltralyticsWeightsDirectory_IsUnderTheUserProfileConfigDirectory()
    {
        var directory = WeightCache.UltralyticsWeightsDirectory();

        Assert.Contains("Ultralytics", directory, StringComparison.Ordinal);
        Assert.EndsWith("weights", directory, StringComparison.Ordinal);
        Assert.True(Path.IsPathRooted(directory));
    }

    [Fact]
    public void LocateAndCache_ReuseExistingWeightsWithoutDownloading()
    {
        var root = Path.Combine(Path.GetTempPath(), "snet-weights-" + Guid.NewGuid().ToString("N"));
        var cache = Path.Combine(root, "cache");
        var work = Path.Combine(root, "work");
        Directory.CreateDirectory(work);
        try
        {
            // 都没有时不能误报存在（否则会跳过必要的下载准备）。
            Assert.Null(WeightCache.Locate(cache, work, "yolo26n.pt"));

            // 工作目录里已下载好的权重应被缓存下来，供后续工程复用。
            var downloaded = Path.Combine(work, "yolo26n.pt");
            File.WriteAllBytes(downloaded, new byte[] { 1, 2, 3, 4 });

            var cached = WeightCache.Cache(cache, work, "yolo26n.pt");

            Assert.Equal(Path.Combine(cache, "yolo26n.pt"), cached);
            Assert.True(File.Exists(cached));
            Assert.Equal(new byte[] { 1, 2, 3, 4 }, File.ReadAllBytes(cached!));

            // 工作目录里的副本优先级最高；换一个新工程（空工作目录）时应该命中缓存而不是重新下载。
            Assert.Equal(downloaded, WeightCache.Locate(cache, work, "yolo26n.pt"));
            var otherProject = Path.Combine(root, "work2");
            Directory.CreateDirectory(otherProject);
            Assert.Equal(Path.Combine(cache, "yolo26n.pt"), WeightCache.Locate(cache, otherProject, "yolo26n.pt"));

            // 重复缓存是幂等的。
            Assert.Equal(Path.Combine(cache, "yolo26n.pt"), WeightCache.Cache(cache, work, "yolo26n.pt"));
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); } catch (IOException) { }
        }
    }

    [Fact]
    public void Cache_ReturnsNullWhenNoWeightsExistAnywhere()
    {
        var root = Path.Combine(Path.GetTempPath(), "snet-weights-empty-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            Assert.Null(WeightCache.Cache(Path.Combine(root, "cache"), Path.Combine(root, "work"), "yolo26n.pt"));
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); } catch (IOException) { }
        }
    }

    /// <summary>
    /// 推理使用独立的 CUDA 13 目录，不能预加载训练环境和旧私有目录中的 CUDA 12/cuDNN。
    /// </summary>
    [Fact]
    public void CudaCandidateDirectories_ExcludeCuda12TrainingAndLegacyRuntimeDirectories()
    {
        var root = Path.Combine(Path.GetTempPath(), "snet-cuda-" + Guid.NewGuid().ToString("N"));
        try
        {
            foreach (var package in new[] { "cublas", "cudnn", "cuda_runtime" })
            {
                Directory.CreateDirectory(Path.Combine(root, "train", "yolo", ".env", "lib", "python3.13", "site-packages", "nvidia", package, "lib"));
            }
            // 另一个 Python 版本也要被扫到
            Directory.CreateDirectory(Path.Combine(root, "train", "anomalib", ".env", "lib", "python3.12", "site-packages", "nvidia", "cufft", "lib"));
            var privateLibrary = Path.Combine(root, "train", "cuda-runtime", "cu13", "nvidia", "cublas", "lib");
            Directory.CreateDirectory(privateLibrary);

            var directories = CudaRuntimeLibraries.CandidateDirectories(root, "/opt/extra", new[] { "/usr/local/cuda/lib64" });

            static string Normalize(string path) => path.Replace('\\', '/');
            Assert.Equal(Path.Combine(root, "train", "cuda-runtime", "cu13"), directories[0]);
            Assert.Contains(privateLibrary, directories);
            Assert.DoesNotContain(Path.Combine(root, "train", "cuda-runtime"), directories);
            Assert.DoesNotContain(directories, directory => Normalize(directory).Contains("/.env/", StringComparison.Ordinal));
            Assert.Contains("/opt/extra", directories);
            Assert.Contains("/usr/local/cuda/lib64", directories);
            // 关键库名单必须覆盖 ONNX Runtime CUDA EP 必需的那几个
            Assert.Contains("libcublasLt.so.13", CudaRuntimeLibraries.CriticalLibraries);
            Assert.Contains("libcufft.so.12", CudaRuntimeLibraries.CriticalLibraries);
            Assert.Contains("libcudnn.so.9", CudaRuntimeLibraries.CriticalLibraries);
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); } catch (IOException) { }
        }
    }

    [Fact]
    public void CudaRuntimeInstaller_UsesOfficialCuda13RuntimePackagesAndCrossPlatformGuidance()
    {
        Assert.Contains(CudaRuntimeInstaller.Packages, package => package.StartsWith("nvidia-cuda-runtime=", StringComparison.Ordinal));
        Assert.Contains(CudaRuntimeInstaller.Packages, package => package.StartsWith("nvidia-cublas=", StringComparison.Ordinal));
        Assert.Contains(CudaRuntimeInstaller.Packages, package => package.StartsWith("nvidia-cudnn-cu13=", StringComparison.Ordinal));
        Assert.DoesNotContain(CudaRuntimeInstaller.Packages, package => package.Contains("cu12", StringComparison.Ordinal));
        Assert.Contains("wsl --update", CudaRuntimeInstaller.MissingDriverMessage(OsKind.Linux, wsl: true), StringComparison.OrdinalIgnoreCase);
        Assert.Contains("NVIDIA", CudaRuntimeInstaller.MissingDriverMessage(OsKind.Windows, wsl: false), StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Fedora", CudaRuntimeInstaller.MissingDriverMessage(OsKind.Linux, wsl: false), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void CudaRuntimeInstaller_ReadyMessageIncludesGpuCompatibilityDetails()
    {
        var gpu = new GpuInfo("NVIDIA GeForce RTX 3060", "8.6", "591.86", 12288, null)
        {
            CudaCoreCount = 3584,
            MaxGraphicsClockMhz = 1777,
        };

        var message = CudaRuntimeInstaller.ReadyMessage(gpu, OsKind.Windows, installed: false);

        Assert.Contains("NVIDIA GeForce RTX 3060", message);
        Assert.Contains("计算能力 8.6", message);
        Assert.Contains("FP32 算力约 12.74 TFLOPS（理论峰值）", message);
        Assert.Contains("推理 CUDA 13.0 / cuDNN 9", message);
        Assert.Contains("训练 CUDA cu128", message);
        Assert.Contains("驱动 591.86", message);
        Assert.DoesNotContain("GPU GPU", message);
    }

    [Theory]
    [InlineData(3584u, 1777u, 12.737536)]
    [InlineData(16384u, 2520u, 82.57536)]
    public void GpuPerformance_CalculatesTheoreticalFp32Tflops(uint cores, uint clockMhz, double expected)
    {
        Assert.Equal(expected, GpuPerformance.CalculateFp32Tflops(cores, clockMhz)!.Value, precision: 6);
        Assert.Null(GpuPerformance.CalculateFp32Tflops(null, clockMhz));
        Assert.Null(GpuPerformance.CalculateFp32Tflops(cores, 0));
    }

    [Fact]
    public void CudaLibraryFiles_NeverLoadSanitizerOrOtherSystemLibraries()
    {
        // 现场事故：候选目录包含 LD_LIBRARY_PATH（conda base）与 /usr/lib/x86_64-linux-gnu，
        // 若把里面的 .so 全部 dlopen，会连 libasan.so.6 一起加载 —— AddressSanitizer 不是第一个
        // 加载的库时会直接 abort 整个进程（ASan runtime does not come first ...）。
        var root = Path.Combine(Path.GetTempPath(), "snet-cuda-files-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var wanted = new[] { "libcublasLt.so.13", "libcublas.so.13.1.1.3", "libcudnn.so.9", "libcudnn_ops.so.9", "libnvJitLink.so.13", "libcudart.so.13", "cudnn_graph64_9.dll", "cublas64_13.dll" };
            var dangerous = new[] { "libasan.so.6", "libtsan.so.2", "libubsan.so.1", "libstdc++.so.6", "libgcc_s.so.1", "libcuda.so.1", "libc.so.6", "libgomp.so.1", "libnvidia-ml.so.1", "libssl.so.3", "libcublas.so.12", "cublas64_12.dll", "libcudart.so.12", "libcudnn.so.90" };
            foreach (var name in wanted.Concat(dangerous).Append("README.md"))
            {
                File.WriteAllText(Path.Combine(root, name), "x");
            }

            // 缺失目录不能抛异常
            var files = CudaRuntimeLibraries.LibraryFiles(new[] { root, Path.Combine(root, "missing") });
            var names = files.Select(Path.GetFileName).ToArray();

            Assert.Equal(wanted.Length, names.Length);
            foreach (var name in wanted) { Assert.Contains(name, names); }
            foreach (var name in dangerous) { Assert.DoesNotContain(name, names); }

            Assert.True(CudaRuntimeLibraries.IsCudaLibrary("libcudnn_graph.so.9"));
            Assert.False(CudaRuntimeLibraries.IsCudaLibrary("libasan.so.6"));
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); } catch (IOException) { }
        }
    }
}
