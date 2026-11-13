namespace Snet.Yolo.Test;

/// <summary>按需生成指定长度的零数据，验证数十 GB 导出时不分配同等大小内存或磁盘文件。</summary>
internal sealed class GeneratedExportReadStream(long length, Action<int>? onRead = null) : Stream
{
    private long _position;
    /// <summary>实际读取次数。</summary>
    public int ReadCount { get; private set; }
    /// <summary>源流是否已释放。</summary>
    public bool Disposed { get; private set; }
    /// <inheritdoc/>
    public override bool CanRead => !Disposed;
    /// <inheritdoc/>
    public override bool CanSeek => false;
    /// <inheritdoc/>
    public override bool CanWrite => false;
    /// <inheritdoc/>
    public override long Length => throw new NotSupportedException();
    /// <inheritdoc/>
    public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
    /// <summary>只允许异步、有界读取；不为完整数据创建数组。</summary>
    public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ObjectDisposedException.ThrowIf(Disposed, this);
        var count = (int)Math.Min(buffer.Length, length - _position);
        buffer.Span[..count].Clear();
        _position += count;
        onRead?.Invoke(++ReadCount);
        return ValueTask.FromResult(count);
    }
    /// <inheritdoc/>
    protected override void Dispose(bool disposing) { Disposed = true; base.Dispose(disposing); }
    /// <inheritdoc/>
    public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException("禁止同步读取。");
    /// <inheritdoc/>
    public override void Flush() => throw new NotSupportedException();
    /// <inheritdoc/>
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    /// <inheritdoc/>
    public override void SetLength(long value) => throw new NotSupportedException();
    /// <inheritdoc/>
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
}
