using System.Net;
using System.Net.Http.Headers;

namespace Snet.Yolo.Server.sam;

public sealed partial class SamModelStore
{
    /// <summary>限制大文件单次传输长度，避免代理必须保持一次超长连接。</summary>
    internal const int DownloadSegmentBytes = 16 * 1024 * 1024;

    /// <summary>分段、有限重试并保留断点；摘要决定缓存名称，完整校验前绝不发布模型。</summary>
    internal async Task EnsureFileAsync(string path, string uri, long size, string digest, Action<int> progress, CancellationToken token)
    {
        if (File.Exists(path))
        {
            if (!await ValidAsync(path, size, digest, token)) { throw new InvalidDataException($"SAM 模型校验失败：{Path.GetFileName(path)}。请移走损坏或不匹配的文件后重试；原文件未修改。"); }
            progress(100); return;
        }
        var temporary = path + "." + digest + ".partial";
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(TimeSpan.FromMinutes(size > 1_000_000_000 ? 30 : 10));
        try
        {
            // FileShare.None 防止两个进程同时追加同一个断点文件；不删除其他进程的缓存。
            await using (var output = new FileStream(temporary, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None, 81920, true))
            {
                if (output.Length > size) { output.SetLength(0); }
                progress((int)(output.Length * 100 / size));
                var failures = 0;
                while (output.Length < size)
                {
                    timeout.Token.ThrowIfCancellationRequested();
                    try
                    {
                        await DownloadSegmentAsync(output, uri, size, progress, timeout.Token);
                        failures = 0;
                    }
                    catch (HttpRequestException error) when (Retryable(error))
                    {
                        if (++failures >= 5)
                        {
                            throw new HttpRequestException($"SAM 模型下载中断：{Path.GetFileName(path)}，已保留 {output.Length:N0}/{size:N0} 字节，下次启用将继续下载。请检查代理和网络。", error);
                        }
                        await Task.Delay(TimeSpan.FromSeconds(Math.Min(8, 1 << (failures - 1))), timeout.Token);
                    }
                }
                await output.FlushAsync(timeout.Token);
            }
            if (!await ValidAsync(temporary, size, digest, timeout.Token))
            {
                File.Delete(temporary); // 只删除本下载器自己的损坏缓存，重新启用可从头获取。
                throw new InvalidDataException($"SAM 模型 SHA-256 校验失败：{Path.GetFileName(path)}。损坏的下载缓存已清理，请重新启用下载。");
            }
            File.Move(temporary, path, false);
            progress(100);
        }
        catch (OperationCanceledException error) when (!token.IsCancellationRequested)
        { throw new HttpRequestException($"SAM 模型下载超时：{Path.GetFileName(path)}。已保留下载断点，请检查网络后重新启用。", error); }
        // 用户取消、网络失败都保留未发布的缓存；最终文件与版本记录保持不变。
    }

    /// <summary>只重试传输错误和暂时性 HTTP 状态；权限、资源不存在等错误直接报告。</summary>
    private static bool Retryable(HttpRequestException error)
        => error.StatusCode is null or HttpStatusCode.RequestTimeout or HttpStatusCode.TooManyRequests
            or HttpStatusCode.InternalServerError or HttpStatusCode.BadGateway or HttpStatusCode.ServiceUnavailable or HttpStatusCode.GatewayTimeout;

    /// <summary>使用可信的固定提交 URL 重新获取重定向；校验 Content-Range，防止错误位置被追加到模型中。</summary>
    private async Task DownloadSegmentAsync(FileStream output, string uri, long size, Action<int> progress, CancellationToken token)
    {
        var offset = output.Length; var end = Math.Min(size - 1, offset + DownloadSegmentBytes - 1);
        using var request = new HttpRequestMessage(HttpMethod.Get, uri);
        request.Headers.Range = new RangeHeaderValue(offset, end);
        request.Headers.AcceptEncoding.Add(new StringWithQualityHeaderValue("identity"));
        using var attempt = CancellationTokenSource.CreateLinkedTokenSource(token);
        attempt.CancelAfter(TimeSpan.FromMinutes(2));
        try
        {
            using var response = await _client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, attempt.Token);
            response.EnsureSuccessStatusCode();
            long expected;
            if (response.StatusCode == HttpStatusCode.PartialContent)
            {
                var range = response.Content.Headers.ContentRange;
                if (range is null || range.Unit != "bytes" || range.From != offset || range.To is null || range.To < offset || range.To > end || range.Length != size)
                { throw new InvalidDataException("SAM 下载源返回了不匹配的 Content-Range，已停止下载以保护模型完整性。"); }
                expected = range.To.Value - offset + 1;
            }
            else if (response.StatusCode == HttpStatusCode.OK)
            {
                // 源站或代理不支持 Range 时安全重下，不能把整文件追加在已有断点后面。
                offset = 0; expected = size;
                attempt.CancelAfter(TimeSpan.FromMinutes(size > 1_000_000_000 ? 30 : 10));
            }
            else { throw new InvalidDataException("SAM 下载源未返回文件或有效分段。"); }
            if (response.Content.Headers.ContentLength is { } length && length != expected)
            { throw new InvalidDataException("SAM 下载响应长度与预期不匹配。"); }
            if (response.StatusCode == HttpStatusCode.OK) { output.SetLength(0); }
            output.Position = offset;
            await using var input = await response.Content.ReadAsStreamAsync(attempt.Token);
            var buffer = new byte[81920]; long received = 0; var previous = -1;
            while (true)
            {
                int read;
                try { read = await input.ReadAsync(buffer, attempt.Token); }
                catch (IOException error) { throw new HttpRequestException("SAM 下载传输流中断。", error); }
                if (read == 0) { break; }
                if (received + read > expected) { throw new InvalidDataException("SAM 下载分段超出预期长度。"); }
                await output.WriteAsync(buffer.AsMemory(0, read), token); received += read;
                var percent = (int)(output.Length * 100 / size);
                if (percent != previous) { progress(percent); previous = percent; }
            }
            if (received != expected) { throw new HttpRequestException("SAM 下载流提前结束，正在恢复断点。"); }
        }
        catch (OperationCanceledException error) when (!token.IsCancellationRequested)
        { throw new HttpRequestException("SAM 下载连接超时，正在重试。", error); }
        finally { await output.FlushAsync(token); }
    }
}
