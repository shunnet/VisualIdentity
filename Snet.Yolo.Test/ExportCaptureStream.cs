namespace Snet.Yolo.Test;

/// <summary>模拟不支持同步写入和定位的 HTTP 响应，只保存 ZIP 首尾元数据，丢弃中间海量内容。</summary>
internal sealed class ExportCaptureStream : Stream
{
    private readonly byte[] _head = new byte[4096];
    private readonly byte[] _tail = new byte[4096];
    /// <summary>实际写入总量，使用 64 位计数。</summary>
    public long Written { get; private set; }
    /// <summary>单次最大写入量。</summary>
    public int MaximumWrite { get; private set; }
    /// <summary>供 ZIP64 读取验证使用的稀疏归档视图。</summary>
    public Stream OpenArchive() => new SparseExportArchiveStream(Written, _head, _tail);
    /// <inheritdoc/>
    public override bool CanRead => false;
    /// <inheritdoc/>
    public override bool CanSeek => false;
    /// <inheritdoc/>
    public override bool CanWrite => true;
    /// <inheritdoc/>
    public override long Length => throw new NotSupportedException();
    /// <inheritdoc/>
    public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
    /// <inheritdoc/>
    public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        MaximumWrite = Math.Max(MaximumWrite, buffer.Length);
        if (Written < _head.Length) { buffer.Span[..(int)Math.Min(buffer.Length, _head.Length - Written)].CopyTo(_head.AsSpan((int)Written)); }
        if (buffer.Length >= _tail.Length) { buffer.Span[^_tail.Length..].CopyTo(_tail); }
        else
        {
            _tail.AsSpan(buffer.Length).CopyTo(_tail);
            buffer.Span.CopyTo(_tail.AsSpan(_tail.Length - buffer.Length));
        }
        Written += buffer.Length;
        return ValueTask.CompletedTask;
    }
    /// <inheritdoc/>
    public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        => WriteAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();
    /// <inheritdoc/>
    public override Task FlushAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    /// <inheritdoc/>
    public override void Flush() => throw new NotSupportedException("HTTP 响应不允许同步刷新。");
    /// <inheritdoc/>
    public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    /// <inheritdoc/>
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    /// <inheritdoc/>
    public override void SetLength(long value) => throw new NotSupportedException();
    /// <inheritdoc/>
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException("HTTP 响应不允许同步写入。");
}
