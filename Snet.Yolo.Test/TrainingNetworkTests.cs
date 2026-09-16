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
}
