using Microsoft.ML.OnnxRuntime;
using SkiaSharp;
using Snet.Yolo.Server.sam;
using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using Xunit;

namespace Snet.Yolo.Test;

/// <summary>使用微型文件与本地 HTTP 替身验证版本事务；不下载真实大模型。</summary>
public sealed class SamVersionTests
{
    private sealed class Fixture : IDisposable
    {
        public string Root { get; } = Path.Combine(Path.GetTempPath(), "sam-version-" + Guid.NewGuid().ToString("N"));
        public byte[] OldBytes { get; } = [1, 2, 3];
        public byte[] NewBytes { get; } = [4, 5, 6];
        public SamModelDefinition Old { get; }
        public SamModelDefinition New { get; }
        public string Head { get; } = new('c', 40);
        public HttpClient Client { get; }
        public List<string> Requests { get; } = [];
        public bool Unknown, Offline, Corrupt, MetadataOffline, OlderHead;
        public Fixture()
        {
            SamModelDefinition Definition(byte[] bytes, char revision) => new(SamModelKind.MobileSam, "test", "", "owner/test", new(revision, 40),
                "encoder.onnx", bytes.Length, Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant(),
                "decoder.onnx", bytes.Length, Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant());
            Old = Definition(OldBytes, 'a'); New = Definition(NewBytes, 'b');
            Client = new(new Handler(request =>
            {
                var path = request.RequestUri!.AbsolutePath; Requests.Add(path);
                if (Offline) { throw new HttpRequestException("offline"); }
                if (path.StartsWith("/api/", StringComparison.Ordinal))
                {
                    if (MetadataOffline) { throw new HttpRequestException("metadata offline"); }
                    var hash = Unknown ? new string('0', 64) : OlderHead ? Old.EncoderHash : New.EncoderHash;
                    return new(HttpStatusCode.OK)
                    {
                        Content = new StringContent(JsonSerializer.Serialize(new
                        {
                            sha = Head,
                            siblings = new[] { new { rfilename = New.Encoder, lfs = new { sha256 = hash } }, new { rfilename = New.Decoder, lfs = new { sha256 = hash } } }
                        }))
                    };
                }
                var bytes = Corrupt ? OldBytes : path.Contains(Head, StringComparison.Ordinal) || path.Contains(New.Revision, StringComparison.Ordinal) ? NewBytes : OldBytes;
                return new(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) };
            }));
        }
        public SamModelStore Store(bool oldOnly = false) => new(Root, oldOnly ? [Old] : [Old, New], Client);
        public async Task SeedAsync()
        {
            Directory.CreateDirectory(Path.Combine(Root, "sam"));
            await File.WriteAllBytesAsync(Path.Combine(Root, "sam", Old.Encoder), OldBytes);
            await File.WriteAllBytesAsync(Path.Combine(Root, "sam", Old.Decoder), OldBytes);
            await Store(oldOnly: true).EnsureReadyAsync();
            Assert.Empty(Requests);
        }
        public void Dispose() { Client.Dispose(); if (Directory.Exists(Root)) { Directory.Delete(Root, true); } }
    }
    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        { token.ThrowIfCancellationRequested(); return Task.FromResult(respond(request)); }
    }

    [Fact]
    public async Task FirstDownload_PinsHeadAndRestartDoesNotAutoUpdate()
    {
        using var f = new Fixture();
        // 程序根目录已存在也必须检查首次下载，不以目录存在代替安装完成。
        Directory.CreateDirectory(Path.Combine(f.Root, "sam"));
        var store = f.Store(); await store.EnsureReadyAsync();
        Assert.Equal(f.Head, store.GetInstalled(SamModelKind.MobileSam).Definition.Revision);
        Assert.Contains(f.Requests, p => p.Contains("/resolve/" + f.Head, StringComparison.Ordinal));
        f.Requests.Clear(); f.Unknown = true;
        var restarted = f.Store(); await restarted.EnsureReadyAsync();
        Assert.Empty(f.Requests); Assert.Equal(f.Head, restarted.GetVersionStatus(SamModelKind.MobileSam).InstalledRevision);
    }

    [Fact]
    public async Task Update_IsValidatedBeforeCommitAndCanRollbackAfterRestart()
    {
        using var f = new Fixture(); await f.SeedAsync(); var store = f.Store();
        var status = await store.CheckForUpdatesAsync(SamModelKind.MobileSam);
        Assert.True(status.CanUpdate); Assert.Equal(f.Old.Revision, status.InstalledRevision);
        Assert.Single(f.Requests); Assert.DoesNotContain(f.Requests, p => p.Contains("/resolve/", StringComparison.Ordinal));
        await store.UpdateAsync(SamModelKind.MobileSam, (candidate, token) =>
        {
            Assert.Equal(f.Old.Revision, store.GetInstalled(SamModelKind.MobileSam).Definition.Revision);
            Assert.Equal(f.NewBytes, File.ReadAllBytes(Path.Combine(candidate.Directory, candidate.Definition.Encoder)));
            Assert.Contains(".versions", candidate.Directory); return Task.CompletedTask;
        });
        Assert.Equal(f.OldBytes, File.ReadAllBytes(Path.Combine(f.Root, "sam", f.Old.Encoder)));
        var restarted = f.Store(); Assert.True(restarted.GetVersionStatus(SamModelKind.MobileSam).CanRollback);
        await restarted.RollbackAsync(SamModelKind.MobileSam, (_, _) => Task.CompletedTask);
        Assert.Equal(f.Old.Revision, f.Store().GetInstalled(SamModelKind.MobileSam).Definition.Revision);
        Assert.True(f.Store().GetVersionStatus(SamModelKind.MobileSam).CanUpdate);
        await restarted.RollbackAsync(SamModelKind.MobileSam, (_, _) => Task.CompletedTask);
        Assert.Equal(f.Head, f.Store().GetInstalled(SamModelKind.MobileSam).Definition.Revision);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FailedOrCancelledValidation_PreservesPointerAndOldFiles(bool cancel)
    {
        using var f = new Fixture(); await f.SeedAsync(); var store = f.Store();
        var pointer = Path.Combine(f.Root, "sam", "active-model.json"); var before = await File.ReadAllTextAsync(pointer);
        using var cts = new CancellationTokenSource();
        var update = store.UpdateAsync(SamModelKind.MobileSam, (_, _) =>
        {
            if (cancel) { cts.Cancel(); return Task.CompletedTask; }
            throw new InvalidDataException("invalid interface");
        }, cancellationToken: cts.Token);
        if (cancel) { await Assert.ThrowsAnyAsync<OperationCanceledException>(() => update); }
        else { await Assert.ThrowsAsync<InvalidDataException>(() => update); }
        Assert.Equal(before, await File.ReadAllTextAsync(pointer));
        Assert.Equal(f.Old.Revision, f.Store().GetVersionStatus(SamModelKind.MobileSam).InstalledRevision);
        Assert.Equal(f.OldBytes, await File.ReadAllBytesAsync(store.GetEncoderPath(SamModelKind.MobileSam)));
    }

    [Fact]
    public async Task UnverifiedUpstream_IsNotDownloadedAndUsesApprovedFallback()
    {
        using var f = new Fixture { Unknown = true }; var store = f.Store();
        var status = await store.CheckForUpdatesAsync(SamModelKind.MobileSam);
        Assert.True(status.UnverifiedUpstream); Assert.False(status.CanUpdate); Assert.False(Directory.Exists(store.DirectoryPath));
        await store.EnsureReadyAsync();
        Assert.Equal(f.New.Revision, store.GetVersionStatus(SamModelKind.MobileSam).InstalledRevision);
        Assert.DoesNotContain(f.Requests, p => p.Contains("/resolve/" + f.Head, StringComparison.Ordinal));
    }

    [Fact]
    public async Task CorruptDownload_NeverPublishesNewPointer()
    {
        using var f = new Fixture(); await f.SeedAsync(); f.Corrupt = true; var validated = false;
        await Assert.ThrowsAsync<InvalidDataException>(() => f.Store().UpdateAsync(SamModelKind.MobileSam, (_, _) => { validated = true; return Task.CompletedTask; }));
        Assert.False(validated); Assert.Equal(f.Old.Revision, f.Store().GetVersionStatus(SamModelKind.MobileSam).InstalledRevision);
        Assert.Empty(Directory.GetFiles(Path.Combine(f.Root, "sam"), "*.partial", SearchOption.AllDirectories));
    }

    [Fact]
    public async Task ConcurrentUpdates_ValidateOnceAndKeepPreviousVersion()
    {
        using var f = new Fixture(); await f.SeedAsync(); var store = f.Store(); var count = 0;
        Task Validate(SamInstalledModel installed, CancellationToken token) { Interlocked.Increment(ref count); return Task.CompletedTask; }
        await Task.WhenAll(store.UpdateAsync(SamModelKind.MobileSam, Validate), store.UpdateAsync(SamModelKind.MobileSam, Validate));
        Assert.Equal(1, count); Assert.True(store.GetVersionStatus(SamModelKind.MobileSam).CanRollback);
    }

    [Fact]
    public async Task OfflineCheck_PreservesInstalledVersionAndReportsFailure()
    {
        using var f = new Fixture(); await f.SeedAsync(); f.Offline = true;
        var status = await f.Store().CheckForUpdatesAsync(SamModelKind.MobileSam);
        Assert.True(status.CheckFailed); Assert.Equal(f.Old.Revision, status.InstalledRevision);
        await f.Store().EnsureReadyAsync(); Assert.Single(f.Requests);
    }

    [Fact]
    public async Task TamperedVersionRecord_IsRejectedBeforeNetworkOrFileAccess()
    {
        using var f = new Fixture(); await f.SeedAsync();
        var pointer = Path.Combine(f.Root, "sam", "active-model.json");
        var json = await File.ReadAllTextAsync(pointer); await File.WriteAllTextAsync(pointer, json.Replace("owner/test", "attacker/test", StringComparison.Ordinal));
        Assert.Throws<InvalidDataException>(() => f.Store().GetInstalled(SamModelKind.MobileSam)); Assert.Empty(f.Requests);
    }

    [Fact]
    public async Task FailedRollback_LeavesCurrentVersionActive()
    {
        using var f = new Fixture(); await f.SeedAsync(); var store = f.Store();
        await store.UpdateAsync(SamModelKind.MobileSam, (_, _) => Task.CompletedTask);
        await Assert.ThrowsAsync<InvalidDataException>(() => store.RollbackAsync(SamModelKind.MobileSam, (_, _) => throw new InvalidDataException("failed")));
        Assert.Equal(f.Head, f.Store().GetVersionStatus(SamModelKind.MobileSam).InstalledRevision);
    }

    [Fact]
    public async Task FirstDownload_MetadataUnavailableUsesPinnedApprovedVersion()
    {
        using var f = new Fixture { MetadataOffline = true }; var store = f.Store();
        await store.EnsureReadyAsync();
        Assert.Equal(f.New.Revision, store.GetVersionStatus(SamModelKind.MobileSam).InstalledRevision);
        Assert.Contains(f.Requests, p => p.Contains("/resolve/" + f.New.Revision, StringComparison.Ordinal));
        Assert.DoesNotContain(f.Requests, p => p.Contains("/resolve/" + f.Head, StringComparison.Ordinal));
    }

    [Fact]
    public async Task OlderUpstreamWeights_DoNotReplaceCatalogRecommendation()
    {
        using var f = new Fixture { OlderHead = true }; await f.SeedAsync(); var store = f.Store();
        var status = await store.CheckForUpdatesAsync(SamModelKind.MobileSam);
        Assert.True(status.CanUpdate); Assert.False(status.UnverifiedUpstream); Assert.Equal(f.New.Revision, status.RecommendedRevision);
        await store.UpdateAsync(SamModelKind.MobileSam, (_, _) => Task.CompletedTask);
        Assert.Equal(f.New.Revision, store.GetVersionStatus(SamModelKind.MobileSam).InstalledRevision);
    }

    [Theory]
    [InlineData(SamModelKind.MobileSam)]
    [InlineData(SamModelKind.Sam21Tiny)]
    public async Task Runtime_RollbackExecutesBothNetworksAndKeepsOldImageVersion(SamModelKind kind)
    {
        // 明确提供本地权重才执行；相同权重的不同提交用于验证版本隔离，不虚构新版效果。
        var source = Environment.GetEnvironmentVariable("SAM_TEST_MODEL_ROOT");
        if (string.IsNullOrWhiteSpace(source)) { return; }
        var root = Path.Combine(Path.GetTempPath(), "sam-version-real-" + Guid.NewGuid().ToString("N"));
        var model = SamModels.Get(kind); var directory = Path.Combine(root, "sam", model.Folder);
        var previous = model with { Revision = new string('e', 40) };
        var previousDirectory = Path.Combine(directory, ".versions", previous.Revision);
        Directory.CreateDirectory(previousDirectory);
        try
        {
            foreach (var name in new[] { model.Encoder, model.Decoder })
            {
                File.Copy(Path.Combine(source, "sam", model.Folder, name), Path.Combine(directory, name));
                File.Copy(Path.Combine(directory, name), Path.Combine(previousDirectory, name));
            }
            await File.WriteAllTextAsync(Path.Combine(directory, "active-model.json"), JsonSerializer.Serialize(new
            {
                Active = new { Definition = model, Legacy = true },
                Previous = new { Definition = previous, Legacy = false }
            }));
            var imagePath = Path.Combine(root, "image.png");
            using (var bitmap = new SKBitmap(320, 240))
            {
                using var canvas = new SKCanvas(bitmap); canvas.Clear(SKColors.White);
                using var paint = new SKPaint { Color = SKColors.Red }; canvas.DrawCircle(160, 120, 60, paint);
                using var image = SKImage.FromBitmap(bitmap); using var png = image.Encode(SKEncodedImageFormat.Png, 100);
                using var file = File.Create(imagePath); png.SaveTo(file);
            }
            var sessions = 0; var store = new SamModelStore(root);
            using var runtime = new SamOnnxRuntime(store, () => { sessions++; return new SessionOptions(); });
            var oldImage = await runtime.EncodeAsync(imagePath, kind);
            Assert.Equal(2, sessions);
            await runtime.RollbackAsync(kind, null);
            Assert.Equal(4, sessions); Assert.Equal(previous.Revision, store.GetInstalled(kind).Definition.Revision);
            Assert.Equal(model.Revision, oldImage.Installed!.Definition.Revision);
            var oldResult = await runtime.SegmentAsync(oldImage, [new(160, 120)]);
            Assert.Equal(6, sessions); Assert.Equal(255, oldResult.Mask[120 * 320 + 160]);
            var newImage = await runtime.EncodeAsync(imagePath, kind);
            Assert.Equal(8, sessions); Assert.Equal(previous.Revision, newImage.Installed!.Definition.Revision);
            var newResult = await runtime.SegmentAsync(newImage, [new(160, 120)]);
            Assert.Equal(8, sessions); Assert.Equal(oldResult.Mask, newResult.Mask);
        }
        finally { Directory.Delete(root, true); }
    }
}
