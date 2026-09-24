namespace Snet.Yolo.Server.models;

/// <summary>工程所属的视觉任务类型。</summary>
public enum ProjectKind
{
    /// <summary>需要人工标注并使用 Ultralytics YOLO 训练的工程。</summary>
    Yolo = 0,

    /// <summary>仅使用正常图片训练 Anomalib 异常检测模型的工程。</summary>
    Anomalib = 1,
}
