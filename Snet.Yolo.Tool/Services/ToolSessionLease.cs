using Snet.Yolo.Server;

namespace Snet.Yolo.Tool.Services;

/// <summary>保护一次识别期间的原生会话，避免切换硬件时提前释放。</summary>
/// <param name="owner">租约所属的运行服务。</param>
/// <param name="entry">需要保护的会话条目。</param>
internal sealed class ToolSessionLease(ToolRuntimeService owner, ToolSessionEntry entry) : IDisposable
{
    private ToolRuntimeService? _owner = owner;

    /// <summary>本次识别使用的固定会话。</summary>
    public IdentityOperate Operate { get; } = entry.Operate;

    /// <summary>幂等归还使用权；最后一个使用者释放已经退役的会话。</summary>
    public void Dispose() => Interlocked.Exchange(ref _owner, null)?.Release(entry);
}
