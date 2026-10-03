using SkiaSharp;
using Snet.Yolo.Server.sam;
using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using Xunit;

namespace Snet.Yolo.Test;

/// <summary>SAM 3 特有的预处理、点提示和双外部权重完整性回归；不联网下载真实模型。</summary>
public sealed class Sam3Tests
{
    [Fact]
    public void Pixels_AreSquareRgbChwNormalized()
    {
        using var bitmap = new SKBitmap(20, 10); bitmap.Erase(SKColors.Red);
        var pixels = SamOnnxRuntime.Sam3Pixels(bitmap, default); var plane = 1008 * 1008;
        Assert.Equal(3 * plane, pixels.Length);
        Assert.All(pixels.Take(plane), v => Assert.Equal(1, v));
        Assert.All(pixels.Skip(plane), v => Assert.Equal(-1, v));
        using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
        Assert.ThrowsAny<OperationCanceledException>(() => SamOnnxRuntime.Sam3Pixels(bitmap, cancelled.Token));
    }

    [Fact]
    public void Points_ScaleEachAxisAndKeepInt64BackgroundLabels()
    {
        var (coordinates, labels) = SamOnnxRuntime.Sam3Points(400, 200, [new(100, 100), new(200, 50, false)]);
        Assert.Equal(new float[] { 252, 504, 504, 252 }, coordinates);
        Assert.Equal(new long[] { 1, 0 }, labels);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ExternalWeights_AreBothVerifiedBeforePublishingVersion(bool corruptDecoder)
    {
        var root = Path.Combine(Path.GetTempPath(), "sam3-store-" + Guid.NewGuid().ToString("N"));
        var bytes = new byte[] { 1, 2, 3 }; var hash = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
        var definition = new SamModelDefinition(SamModelKind.Sam3, "test", "sam3", "owner/test", new('a', 40),
            "onnx/encoder.onnx", 3, hash, "onnx/decoder.onnx", 3, hash,
            EncoderData: "onnx/encoder.onnx_data", EncoderDataBytes: 3, EncoderDataHash: hash,
            DecoderData: "onnx/decoder.onnx_data", DecoderDataBytes: 3, DecoderDataHash: hash);
        var requests = new List<string>();
        using var client = new HttpClient(new Handler(request =>
        {
            var path = request.RequestUri!.AbsolutePath; requests.Add(path);
            if (path.StartsWith("/api/", StringComparison.Ordinal)) { return new(HttpStatusCode.ServiceUnavailable); }
            return new(HttpStatusCode.OK) { Content = new ByteArrayContent(corruptDecoder && path.EndsWith("decoder.onnx_data", StringComparison.Ordinal) ? [3, 2, 1] : bytes) };
        }));
        try
        {
            var store = new SamModelStore(root, [definition], client);
            if (corruptDecoder)
            {
                await Assert.ThrowsAsync<InvalidDataException>(() => store.EnsureReadyAsync(SamModelKind.Sam3));
                Assert.False(File.Exists(Path.Combine(root, "sam/sam3/active-model.json")));
                Assert.False(File.Exists(store.GetEncoderPath(SamModelKind.Sam3)));
                Assert.Empty(Directory.GetFiles(root, "*.partial", SearchOption.AllDirectories));
            }
            else
            {
                await store.EnsureReadyAsync(SamModelKind.Sam3);
                Assert.Equal(bytes, await File.ReadAllBytesAsync(Path.Combine(root, "sam/sam3/onnx/decoder.onnx_data")));
                Assert.True(File.Exists(Path.Combine(root, "sam/sam3/active-model.json")));
                Assert.EndsWith("encoder.onnx_data", requests[1]); Assert.EndsWith("decoder.onnx_data", requests[2]);
                requests.Clear(); await new SamModelStore(root, [definition], client).EnsureReadyAsync(SamModelKind.Sam3);
                Assert.Empty(requests);
            }
        }
        finally { if (Directory.Exists(root)) { Directory.Delete(root, true); } }
    }

    [Fact]
    public async Task ChangedExternalWeights_AreNotTreatedAsCompatibleUpstream()
    {
        var definition = SamModels.Get(SamModelKind.Sam3);
        var files = new[] {
            (definition.Encoder, definition.EncoderHash), (definition.Decoder, definition.DecoderHash),
            (definition.EncoderData!, definition.EncoderDataHash!), (definition.DecoderData!, new string('0', 64)) };
        using var client = new HttpClient(new Handler(_ => new(HttpStatusCode.OK)
        {
            Content = new StringContent(JsonSerializer.Serialize(new {
                sha = new string('a', 40), siblings = files.Select(f => new { rfilename = f.Item1, lfs = new { sha256 = f.Item2 } }) }))
        }));
        var root = Path.Combine(Path.GetTempPath(), "sam3-check-" + Guid.NewGuid().ToString("N"));
        var store = new SamModelStore(root, [definition], client);
        var status = await store.CheckForUpdatesAsync(SamModelKind.Sam3);
        Assert.True(status.UnverifiedUpstream); Assert.False(status.CanUpdate); Assert.False(Directory.Exists(root));
    }

    /// <summary>本地 HTTP 替身，确保常规测试不会访问模型仓库。</summary>
    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        { token.ThrowIfCancellationRequested(); return Task.FromResult(respond(request)); }
    }
}
