using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using Snet.Yolo.Server.sam;
using Xunit;

namespace Snet.Yolo.Test;

/// <summary>用确定性的传输中断复现 EOF，验证续传、取消、服务端 Range 行为与最终完整性。</summary>
public sealed class SamDownloadTests
{
    /// <summary>显式提供测试目录才访问真实固定权重；日常回归不联网。</summary>
    [Fact]
    public async Task RealPinnedSam3DecoderData_DownloadsAndVerifiesAcrossSegments()
    {
        var root = Environment.GetEnvironmentVariable("SAM_TEST_DOWNLOAD_ROOT");
        if (string.IsNullOrWhiteSpace(root)) { return; }
        Directory.CreateDirectory(root);
        var model = SamModels.Get(SamModelKind.Sam3);
        using var client = new HttpClient();
        var store = new SamModelStore(root, SamModels.All, client);
        await store.EnsureFileAsync(Path.Combine(root, "sam3-decoder.onnx_data"),
            $"https://huggingface.co/{model.Repository}/resolve/{model.Revision}/{model.DecoderData}",
            model.DecoderDataBytes, model.DecoderDataHash!, _ => { }, default);
        Assert.Equal(model.DecoderDataBytes, new FileInfo(Path.Combine(root, "sam3-decoder.onnx_data")).Length);
    }

    [Fact]
    public async Task Eof_RetriesAtSavedOffsetAndPublishesOnlyVerifiedFile()
    {
        var bytes = Enumerable.Range(0, 10).Select(i => (byte)i).ToArray(); var calls = 0;
        using var fixture = new Fixture(bytes, request =>
        {
            var start = request.Headers.Range!.Ranges.Single().From!.Value;
            if (++calls == 1) { Assert.Equal(0, start); return Partial(new BrokenStream(bytes, 3, true), 0, 9, 10); }
            Assert.Equal(3, start); return Partial(new MemoryStream(bytes[3..]), 3, 9, 10);
        });
        await fixture.DownloadAsync();
        Assert.Equal(2, calls); Assert.Equal(bytes, await File.ReadAllBytesAsync(fixture.Path));
        Assert.Empty(Directory.GetFiles(fixture.Root, "*.partial"));
    }

    [Fact]
    public async Task Cancellation_RetainsPrefixAndNewStoreResumesWithoutStartingOver()
    {
        byte[] bytes = [1, 2, 3, 4, 5, 6]; var calls = 0;
        using var cancellation = new CancellationTokenSource();
        using var fixture = new Fixture(bytes, request =>
        {
            var start = request.Headers.Range!.Ranges.Single().From!.Value;
            if (++calls == 1) { return Partial(new BrokenStream(bytes, 3, false), 0, 5, 6); }
            Assert.Equal(3, start); return Partial(new MemoryStream(bytes[3..]), 3, 5, 6);
        });
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => fixture.DownloadAsync(p => { if (p >= 50) { cancellation.Cancel(); } }, cancellation.Token));
        Assert.False(File.Exists(fixture.Path));
        Assert.Equal(3, new FileInfo(Assert.Single(Directory.GetFiles(fixture.Root, "*.partial"))).Length);
        await fixture.DownloadAsync(); Assert.Equal(bytes, await File.ReadAllBytesAsync(fixture.Path)); Assert.Equal(2, calls);
    }

    [Fact]
    public async Task ServerIgnoringRange_RestartsRatherThanAppendingEntireFile()
    {
        byte[] bytes = [1, 2, 3, 4];
        using var fixture = new Fixture(bytes, request =>
        {
            Assert.Equal(2, request.Headers.Range!.Ranges.Single().From);
            return new(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) };
        });
        await File.WriteAllBytesAsync(fixture.PartialPath, bytes[..2]);
        await fixture.DownloadAsync(); Assert.Equal(bytes, await File.ReadAllBytesAsync(fixture.Path));
    }

    [Fact]
    public async Task InvalidRange_DoesNotAppendOrPublish()
    {
        byte[] bytes = [1, 2, 3, 4];
        using var fixture = new Fixture(bytes, _ => Partial(new MemoryStream(bytes), 1, 4, 5));
        await Assert.ThrowsAsync<InvalidDataException>(() => fixture.DownloadAsync());
        Assert.False(File.Exists(fixture.Path)); Assert.Equal(0, new FileInfo(fixture.PartialPath).Length);
    }

    [Fact]
    public async Task InvalidFullResponseLength_DoesNotDestroySavedPrefix()
    {
        byte[] bytes = [1, 2, 3, 4];
        using var fixture = new Fixture(bytes, _ => new(HttpStatusCode.OK) { Content = new ByteArrayContent([9]) });
        await File.WriteAllBytesAsync(fixture.PartialPath, bytes[..2]);
        await Assert.ThrowsAsync<InvalidDataException>(() => fixture.DownloadAsync());
        Assert.Equal(bytes[..2], await File.ReadAllBytesAsync(fixture.PartialPath)); Assert.False(File.Exists(fixture.Path));
    }

    [Fact]
    public async Task HashFailure_DeletesOnlyOwnedCorruptCache()
    {
        byte[] bytes = [1, 2, 3];
        using var fixture = new Fixture(bytes, _ => new(HttpStatusCode.OK) { Content = new ByteArrayContent([3, 2, 1]) });
        await Assert.ThrowsAsync<InvalidDataException>(() => fixture.DownloadAsync());
        Assert.False(File.Exists(fixture.Path)); Assert.False(File.Exists(fixture.PartialPath));
    }

    [Fact]
    public async Task LargeFile_UsesBoundedRangesAndPreservesExactBytes()
    {
        var bytes = new byte[SamModelStore.DownloadSegmentBytes + 5]; bytes[^1] = 42; var calls = 0;
        using var fixture = new Fixture(bytes, request =>
        {
            var range = request.Headers.Range!.Ranges.Single(); var start = (int)range.From!.Value; var end = (int)range.To!.Value;
            Assert.InRange(end - start + 1, 1, SamModelStore.DownloadSegmentBytes); calls++;
            return Partial(new MemoryStream(bytes[start..(end + 1)]), start, end, bytes.Length);
        });
        await fixture.DownloadAsync(); Assert.Equal(2, calls); Assert.Equal(bytes, await File.ReadAllBytesAsync(fixture.Path));
    }

    [Fact]
    public async Task NotFound_IsNotRetried()
    {
        var calls = 0;
        using var fixture = new Fixture([1], _ => { calls++; return new(HttpStatusCode.NotFound); });
        await Assert.ThrowsAsync<HttpRequestException>(() => fixture.DownloadAsync()); Assert.Equal(1, calls);
    }

    [Fact]
    public async Task RepeatedEof_StopsAfterFiveAttemptsWithFileAndResumeMessage()
    {
        var calls = 0;
        using var fixture = new Fixture([1, 2, 3], _ => { calls++; throw new HttpRequestException("transport EOF"); });
        var error = await Assert.ThrowsAsync<HttpRequestException>(() => fixture.DownloadAsync());
        Assert.Equal(5, calls); Assert.Contains("weights.bin", error.Message); Assert.Contains("下次启用将继续下载", error.Message);
        Assert.False(File.Exists(fixture.Path)); Assert.True(File.Exists(fixture.PartialPath));
    }

    /// <summary>构造精确范围响应；StreamContent 不会掩盖测试流的意外 EOF。</summary>
    private static HttpResponseMessage Partial(Stream stream, long start, long end, long size)
    {
        var response = new HttpResponseMessage(HttpStatusCode.PartialContent) { Content = new StreamContent(stream) };
        response.Content.Headers.ContentRange = new ContentRangeHeaderValue(start, end, size);
        response.Content.Headers.ContentLength = end - start + 1; return response;
    }

    /// <summary>每次调用使用新 Store，验证断点不依赖进程内状态。</summary>
    private sealed class Fixture : IDisposable
    {
        public string Root { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "sam-download-" + Guid.NewGuid().ToString("N"));
        public string Path => System.IO.Path.Combine(Root, "weights.bin");
        public string PartialPath => Path + "." + Digest + ".partial";
        private string Digest { get; }
        private readonly byte[] bytes;
        private readonly HttpClient client;
        public Fixture(byte[] bytes, Func<HttpRequestMessage, HttpResponseMessage> respond)
        {
            this.bytes = bytes; Digest = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
            client = new(new Handler(respond)); Directory.CreateDirectory(Root);
        }
        public Task DownloadAsync(Action<int>? progress = null, CancellationToken token = default)
            => new SamModelStore(Root, SamModels.All, client).EnsureFileAsync(Path, "https://example.test/weights", bytes.LongLength, Digest, progress ?? (_ => { }), token);
        public void Dispose() { client.Dispose(); Directory.Delete(Root, true); }
    }

    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        { token.ThrowIfCancellationRequested(); return Task.FromResult(respond(request)); }
    }

    /// <summary>先交付指定前缀，再模拟传输异常或正常提前结束；支持取消测试。</summary>
    private sealed class BrokenStream(byte[] bytes, int prefix, bool throws) : MemoryStream(bytes)
    {
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken token = default)
        {
            token.ThrowIfCancellationRequested();
            if (Position >= prefix)
            {
                if (throws) { throw new IOException("Received an unexpected EOF or 0 bytes from the transport stream."); }
                return ValueTask.FromResult(0);
            }
            return base.ReadAsync(buffer[..Math.Min(buffer.Length, prefix - (int)Position)], token);
        }
    }
}
