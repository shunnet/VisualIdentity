using Microsoft.ML.OnnxRuntime;

namespace Snet.Yolo.Server.anomalib;

/// <summary>已校验的原生模型会话及其语义契约。</summary>
/// <param name="Session">由缓存租约负责释放的原生会话。</param>
/// <param name="Manifest">对应模型清单。</param>
/// <param name="Contract">实际输出节点的语义契约。</param>
internal sealed record AnomalibModelSession(InferenceSession Session, AnomalibModelManifest Manifest, DiscoveredAnomalibOutputContract Contract);
