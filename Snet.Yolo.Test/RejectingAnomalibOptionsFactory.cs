using Microsoft.ML.OnnxRuntime;
using Snet.Yolo.Server.anomalib;

namespace Snet.Yolo.Test;

/// <summary>记录页面是否进入真实识别入口，在创建原生会话之前返回受控失败。</summary>
internal sealed class RejectingAnomalibOptionsFactory : IAnomalibSessionOptionsFactory
{
    /// <summary>识别入口捕获设备配置的次数。</summary>
    public int Attempts { get; private set; }

    /// <summary>记录识别调用，避免测试加载模型或占用 CPU/GPU 执行推理。</summary>
    public IAnomalibSessionOptionsFactory Capture()
    {
        Attempts++;
        throw new InvalidOperationException("受控识别入口检查");
    }

    /// <summary>测试不创建原生 ONNX Runtime 会话。</summary>
    public SessionOptions Create() => throw new InvalidOperationException("测试不应创建原生会话。");
}
