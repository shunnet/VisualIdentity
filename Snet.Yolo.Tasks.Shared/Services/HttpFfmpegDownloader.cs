using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using System.IO.Compression;
using System.Net;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;

namespace Snet.Yolo.Tasks.Services;

/// <summary>
/// 从 GitHub（GyanD/codexffmpeg 的 releases）下载最新版 FFmpeg 压缩包。
/// 网络策略与训练一致：沿用环境代理；配置了 Training:Proxy / Training:CaBundle 时用于企业代理与 HTTPS 拦截。
/// </summary>
public sealed class HttpFfmpegDownloader : IFfmpegDownloader, IDisposable
{
    private readonly MediaToolOptions _options;
    private readonly string? _proxy;
    private readonly string? _caBundle;
    private readonly Lazy<HttpClient> _client;
    private bool _disposed;

    /// <summary>创建下载器。</summary>
    public HttpFfmpegDownloader(IOptions<MediaToolOptions> options, IConfiguration configuration)
    {
        _options = options.Value;
        _proxy = configuration["Training:Proxy"];
        _caBundle = configuration["Training:CaBundle"];
        _client = new Lazy<HttpClient>(CreateClient, LazyThreadSafetyMode.ExecutionAndPublication);
    }

    /// <summary>下载最新版 FFmpeg 压缩包（优先 essentials_build 的 zip）。</summary>
    public async Task<(string ArchivePath, string Version)> DownloadLatestAsync(string destinationDirectory, Action<long, long?> onProgress, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(destinationDirectory);
        var (version, url, fileName) = await ResolveLatestAssetAsync(cancellationToken);
        var archivePath = Path.Combine(destinationDirectory, fileName);
        using (var response = await _client.Value.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken))
        {
            response.EnsureSuccessStatusCode();
            var total = response.Content.Headers.ContentLength;
            await using var source = await response.Content.ReadAsStreamAsync(cancellationToken);
            await using var destination = new FileStream(archivePath, FileMode.Create, FileAccess.Write, FileShare.None, 128 * 1024, FileOptions.Asynchronous);
            var buffer = new byte[128 * 1024];
            long written = 0;
            int read;
            while ((read = await source.ReadAsync(buffer, cancellationToken)) > 0)
            {
                await destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                written += read;
                onProgress(written, total);
            }
        }
        return (archivePath, version);
    }

    /// <summary>查询最新发布并选出与当前架构匹配的 Windows 压缩包。</summary>
    private async Task<(string Version, string Url, string FileName)> ResolveLatestAssetAsync(CancellationToken cancellationToken)
    {
        using var response = await _client.Value.GetAsync(_options.ResolveReleaseApiUrl(), cancellationToken);
        response.EnsureSuccessStatusCode();
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
        var root = document.RootElement;
        var tag = root.TryGetProperty("tag_name", out var tagElement) ? tagElement.GetString() ?? "latest" : "latest";
        if (!root.TryGetProperty("assets", out var assets) || assets.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidOperationException("GitHub 返回的发布信息里没有下载文件（assets）。");
        }

        var candidates = new List<(string Name, string Url)>();
        foreach (var asset in assets.EnumerateArray())
        {
            var name = asset.TryGetProperty("name", out var nameElement) ? nameElement.GetString() : null;
            var url = asset.TryGetProperty("browser_download_url", out var urlElement) ? urlElement.GetString() : null;
            if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(url)) { continue; }
            if (!name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)) { continue; }   // 只取 zip，避免依赖 7z
            candidates.Add((name, url!));
        }
        if (candidates.Count == 0) { throw new InvalidOperationException("发布信息里没有 .zip 安装包。"); }

        var wants32 = RuntimeInformation.OSArchitecture is Architecture.X86 or Architecture.Arm;
        bool MatchesArchitecture(string name)
        {
            var lower = name.ToLowerInvariant();
            if (wants32) { return lower.Contains("win32"); }
            return lower.Contains("win64") || !lower.Contains("win32");
        }
        // 优先级：essentials（体积小、够用）> 架构匹配 > full
        var chosen = candidates
            .Where(item => MatchesArchitecture(item.Name))
            .OrderByDescending(item => item.Name.Contains("essentials", StringComparison.OrdinalIgnoreCase))
            .ThenBy(item => item.Name.Length)
            .FirstOrDefault();
        if (chosen.Name is null) { chosen = candidates[0]; }
        return (tag, chosen.Url, chosen.Name);
    }

    private HttpClient CreateClient()
    {
        var handler = new HttpClientHandler { AllowAutoRedirect = true };
        if (!string.IsNullOrWhiteSpace(_proxy))
        {
            handler.Proxy = new WebProxy(_proxy.Trim());
            handler.UseProxy = true;
        }
        if (!string.IsNullOrWhiteSpace(_caBundle) && File.Exists(_caBundle.Trim()))
        {
            // 企业代理做 HTTPS 拦截时补上正确的 CA（不关闭证书校验）
            var certificates = new X509Certificate2Collection();
            certificates.ImportFromPemFile(_caBundle.Trim());
            handler.ServerCertificateCustomValidationCallback = (_, certificate, chain, errors) =>
            {
                if (errors == System.Net.Security.SslPolicyErrors.None) { return true; }
                if (certificate is null) { return false; }
                using var custom = new X509Chain();
                custom.ChainPolicy.TrustMode = X509ChainTrustMode.CustomRootTrust;
                custom.ChainPolicy.CustomTrustStore.AddRange(certificates);
                custom.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck;
                return custom.Build(new X509Certificate2(certificate));
            };
        }
        var client = new HttpClient(handler) { Timeout = TimeSpan.FromMinutes(30) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("Snet.Yolo.Tasks");
        client.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        return client;
    }

    /// <summary>释放 HttpClient。</summary>
    public void Dispose()
    {
        if (_disposed) { return; }
        _disposed = true;
        if (_client.IsValueCreated) { _client.Value.Dispose(); }
    }
}

/// <summary>解压 FFmpeg 压缩包：把 bin/ffmpeg(.exe) 与 bin/ffprobe(.exe) 平铺到目标目录。</summary>
public static class FfmpegArchiveExtractor
{
    /// <summary>解压所需可执行文件，返回是否两个文件都解压成功。</summary>
    /// <param name="archivePath">zip 路径。</param>
    /// <param name="targetDirectory">目标目录（不存在会自动创建）。</param>
    /// <param name="onFile">每解压一个文件回调一次（文件名）。</param>
    public static bool Extract(string archivePath, string targetDirectory, Action<string>? onFile = null)
    {
        Directory.CreateDirectory(targetDirectory);
        var wanted = OperatingSystem.IsWindows() ? new[] { "ffmpeg.exe", "ffprobe.exe" } : new[] { "ffmpeg", "ffprobe" };
        var extracted = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        using var archive = ZipFile.OpenRead(archivePath);
        foreach (var entry in archive.Entries)
        {
            var name = Path.GetFileName(entry.FullName);
            if (string.IsNullOrEmpty(name) || !wanted.Contains(name, StringComparer.OrdinalIgnoreCase)) { continue; }
            if (entry.FullName.Contains("/bin/", StringComparison.OrdinalIgnoreCase) || entry.FullName.StartsWith("bin/", StringComparison.OrdinalIgnoreCase))
            {
                var target = Path.Combine(targetDirectory, name);
                entry.ExtractToFile(target, overwrite: true);
                extracted.Add(name);
                onFile?.Invoke(name);
            }
        }
        return wanted.All(extracted.Contains);
    }
}
