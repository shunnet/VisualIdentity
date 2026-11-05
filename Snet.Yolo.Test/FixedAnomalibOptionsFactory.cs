using Microsoft.ML.OnnxRuntime;
using Snet.Yolo.Server.anomalib;

namespace Snet.Yolo.Test;

/// <summary>测试用固定设备快照。</summary>
/// <param name="cacheKey">不可变的设备标识。</param>
/// <param name="created">记录实际选项创建次数。</param>
internal sealed class FixedAnomalibOptionsFactory(string cacheKey, Action created) : IAnomalibSessionOptionsFactory
{
    /// <summary>该快照的设备标识。</summary>
    public string CacheKey { get; } = cacheKey;
    /// <summary>创建可在无 GPU 机器运行的真实原生选项。</summary>
    /// <returns>CPU 会话选项。</returns>
    public SessionOptions Create() { created(); return new SessionOptions(); }
}
