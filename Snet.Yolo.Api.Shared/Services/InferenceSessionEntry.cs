using Snet.Yolo.Server;

namespace Snet.Yolo.Api.Services;

/// <summary>由缓存锁保护的会话状态，活动租约结束前不可释放。</summary>
/// <param name="key">稳定会话缓存键。</param>
/// <param name="value">线程安全的一次性会话加载器。</param>
internal sealed class InferenceSessionEntry(string key, Lazy<IdentityOperate> value)
{
    /// <summary>设备、模型及参数组成的缓存键。</summary>
    public string Key { get; } = key;
    /// <summary>延迟创建的原生识别操作。</summary>
    public Lazy<IdentityOperate> Value { get; } = value;
    /// <summary>正在使用此条目的请求数量。</summary>
    public int Users { get; set; }
    /// <summary>是否已经从缓存退役。</summary>
    public bool Retired { get; set; }
    /// <summary>是否已经取得一次性的释放权。</summary>
    public bool Disposed { get; set; }
}
