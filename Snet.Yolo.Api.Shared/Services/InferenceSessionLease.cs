using Snet.Yolo.Server;

namespace Snet.Yolo.Api.Services;

/// <summary>保持识别会话有效，直到当前请求完成。</summary>
public sealed class InferenceSessionLease : IDisposable
{
    private InferenceSessionCache? _owner;
    private readonly InferenceSessionEntry _entry;

    /// <summary>为已成功加载的会话建立活动租约。</summary>
    /// <param name="owner">会话所属缓存。</param>
    /// <param name="entry">受保护的缓存条目。</param>
    /// <param name="operate">已经初始化的识别操作。</param>
    internal InferenceSessionLease(InferenceSessionCache owner, InferenceSessionEntry entry, IdentityOperate operate)
    {
        _owner = owner;
        _entry = entry;
        Operate = operate;
    }

    /// <summary>当前请求使用的识别操作。</summary>
    public IdentityOperate Operate { get; }
    /// <summary>幂等归还引用；最后一个使用者负责释放已退役会话。</summary>
    public void Dispose() => Interlocked.Exchange(ref _owner, null)?.Release(_entry);
}
