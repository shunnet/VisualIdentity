namespace Snet.Yolo.Test;

/// <summary>允许读取 ZIP 目录和小型头部，在大块图片读取处阻塞以验证解压中取消及临时文件清理。</summary>
internal sealed class BlockingArchiveStream(byte[] archive) : MemoryStream(archive, writable: false)
{
    /// <summary>开始读取图片数据时完成，测试无需依赖定时等待。</summary>
    public TaskCompletionSource DataReadStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    /// <inheritdoc/>
    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        if (buffer.Length > 1024)
        {
            DataReadStarted.TrySetResult();
            if (!cancellationToken.CanBeCanceled) { throw new InvalidOperationException("解压读取必须携带取消令牌。"); }
            await Task.Delay(Timeout.Infinite, cancellationToken);
        }
        return await base.ReadAsync(buffer, cancellationToken);
    }

    /// <inheritdoc/>
    public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        => ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();
}
