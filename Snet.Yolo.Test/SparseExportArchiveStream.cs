namespace Snet.Yolo.Test;

/// <summary>通过真实归档长度及首尾缓冲提供可定位读取，不为 50 GiB ZIP 分配内存。</summary>
internal sealed class SparseExportArchiveStream(long length, byte[] head, byte[] tail) : Stream
{
    /// <inheritdoc/>
    public override bool CanRead => true;
    /// <inheritdoc/>
    public override bool CanSeek => true;
    /// <inheritdoc/>
    public override bool CanWrite => false;
    /// <inheritdoc/>
    public override long Length => length;
    /// <inheritdoc/>
    public override long Position { get; set; }
    /// <inheritdoc/>
    public override int Read(byte[] buffer, int offset, int count)
    {
        count = (int)Math.Min(count, Math.Max(0, length - Position));
        var target = buffer.AsSpan(offset, count);
        target.Clear();
        for (var i = 0; i < count; i++)
        {
            var position = Position + i;
            if (position < head.Length) { target[i] = head[position]; }
            if (position >= length - tail.Length) { target[i] = tail[position - (length - tail.Length)]; }
        }
        Position += count;
        return count;
    }
    /// <inheritdoc/>
    public override long Seek(long offset, SeekOrigin origin)
    {
        Position = origin switch { SeekOrigin.Begin => offset, SeekOrigin.Current => Position + offset, SeekOrigin.End => length + offset, _ => throw new ArgumentOutOfRangeException(nameof(origin)) };
        return Position;
    }
    /// <inheritdoc/>
    public override void Flush() => throw new NotSupportedException();
    /// <inheritdoc/>
    public override void SetLength(long value) => throw new NotSupportedException();
    /// <inheritdoc/>
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
}
