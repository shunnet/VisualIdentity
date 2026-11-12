namespace Snet.Yolo.Tasks.Core.Training;

/// <summary>训练推荐值涉及的设置项。</summary>
public enum YoloAdviceField
{
    /// <summary>输入尺寸（imgsz）。</summary>
    ImageSize,
    /// <summary>模型规格。</summary>
    Model,
    /// <summary>训练轮数。</summary>
    Epochs,
    /// <summary>是否使用验证集。</summary>
    Validation,
}
