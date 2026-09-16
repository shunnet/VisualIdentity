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
        var candidates = WeightCache.Candidates(@"C:\app\train\weights", @"C:\app\train\users\snet\p1", "yolo26n.pt");

        Assert.Equal(3, candidates.Count);
        Assert.Equal(Path.Combine(@"C:\app\train\users\snet\p1", "yolo26n.pt"), candidates[0]);
        Assert.Equal(Path.Combine(@"C:\app\train\weights", "yolo26n.pt"), candidates[1]);
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
    /// Linux 上 CUDA 运行库常常只存在于训练 venv（pip 的 nvidia-* 包）里，
    /// ONNX Runtime 默认不会去那里找，于是验证页报 libcublasLt.so.12 找不到。
    /// 这里锁定“候选目录必须包含 venv 内各 nvidia 包的 lib 目录”。
    /// </summary>
    [Fact]
    public void CudaCandidateDirectories_IncludeLibrariesInstalledInsideTheTrainingVenv()
    {
        var root = Path.Combine(Path.GetTempPath(), "snet-cuda-" + Guid.NewGuid().ToString("N"));
        try
        {
            foreach (var package in new[] { "cublas", "cudnn", "cuda_runtime" })
            {
                Directory.CreateDirectory(Path.Combine(root, "train", ".env", "lib", "python3.13", "site-packages", "nvidia", package, "lib"));
            }
            // 另一个 Python 版本也要被扫到
            Directory.CreateDirectory(Path.Combine(root, "train", ".env", "lib", "python3.12", "site-packages", "nvidia", "cufft", "lib"));

            var directories = CudaRuntimeLibraries.CandidateDirectories(root, "/opt/extra", new[] { "/usr/local/cuda/lib64" });

            static string Normalize(string path) => path.Replace('\\', '/');
            Assert.Equal(root, directories[0]);
            Assert.Contains(directories, directory => Normalize(directory).EndsWith("nvidia/cublas/lib"));
            Assert.Contains(directories, directory => Normalize(directory).EndsWith("nvidia/cudnn/lib"));
            Assert.Contains(directories, directory => Normalize(directory).EndsWith("nvidia/cufft/lib"));
            Assert.Contains("/opt/extra", directories);
            Assert.Contains("/usr/local/cuda/lib64", directories);
            // 关键库名单必须覆盖 ONNX Runtime CUDA EP 必需的那几个
            Assert.Contains("libcublasLt.so.12", CudaRuntimeLibraries.CriticalLibraries);
            Assert.Contains("libcudnn.so.9", CudaRuntimeLibraries.CriticalLibraries);
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); } catch (IOException) { }
        }
    }

    [Fact]
    public void CudaLibraryFiles_OnlyPickSharedObjectsAndSurviveMissingDirectories()
    {
        var root = Path.Combine(Path.GetTempPath(), "snet-cuda-files-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            File.WriteAllText(Path.Combine(root, "libcublasLt.so.12"), "x");
            File.WriteAllText(Path.Combine(root, "README.md"), "x");

            var files = CudaRuntimeLibraries.LibraryFiles(new[] { root, Path.Combine(root, "missing") });

            Assert.Single(files);
            Assert.EndsWith("libcublasLt.so.12", files[0]);
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); } catch (IOException) { }
        }
    }
}
