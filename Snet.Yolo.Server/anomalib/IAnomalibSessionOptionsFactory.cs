using Microsoft.ML.OnnxRuntime;

namespace Snet.Yolo.Server.anomalib;

/// <summary>为 Anomalib 提供会话选项及设备快照；固定硬件宿主可沿用默认缓存标识。</summary>
public interface IAnomalibSessionOptionsFactory
{
    /// <summary>会话设备标识；固定硬件宿主默认使用空标识。</summary>
    string CacheKey => string.Empty;
    /// <summary>捕获不可变设备配置，确保缓存标识和会话创建使用同一硬件。</summary>
    /// <returns>固定配置的选项工厂；固定硬件实现可返回自身。</returns>
    IAnomalibSessionOptionsFactory Capture() => this;
    /// <summary>创建由推理服务释放的会话选项。</summary>
    /// <returns>新建的原生会话选项。</returns>
    SessionOptions Create();
}
