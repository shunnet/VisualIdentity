namespace Snet.Yolo.Tasks.Core.Training;

/// <summary>训练生命周期阶段。</summary>
public enum TrainingPhase
{
    /// <summary>No training is running.</summary>
    Idle,
    /// <summary>Dataset export is running.</summary>
    Preparing,        // 导出数据集
    /// <summary>The Python and accelerator environment is being checked.</summary>
    EnvironmentCheck, // 检测训练环境
    /// <summary>Training dependencies are being installed.</summary>
    Installing,       // 搭建训练环境
    /// <summary>Ultralytics training is running.</summary>
    Training,         // 训练中
    /// <summary>The trained model is being validated.</summary>
    Validating,       // 验证模型
    /// <summary>Training completed successfully.</summary>
    Complete,
    /// <summary>Training failed.</summary>
    Failed,
    /// <summary>Training was cancelled.</summary>
    Cancelled,
}
