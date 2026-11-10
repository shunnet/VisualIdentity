namespace Snet.Yolo.Tasks.Services;

/// <summary>视频后台任务当前所处的处理阶段。</summary>
public enum VideoRecognitionStage
{
    /// <summary>任务正在等待后台工作器。</summary>
    Queued,
    /// <summary>正在读取视频信息并抽取帧。</summary>
    Decoding,
    /// <summary>正在加载 ONNX 模型。</summary>
    LoadingModel,
    /// <summary>正在逐帧执行推理。</summary>
    Recognizing,
    /// <summary>正在编码带标注的视频。</summary>
    Encoding,
    /// <summary>任务已经成功完成。</summary>
    Completed,
    /// <summary>任务被用户取消。</summary>
    Cancelled,
    /// <summary>任务执行失败。</summary>
    Failed,
}
