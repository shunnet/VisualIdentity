namespace Snet.Yolo.Server.anomalib;

using System.Text.Json;
using System.Text.Json.Serialization;

/// <summary>Anomalib 训练生命周期阶段。</summary>
public enum AnomalibTrainingPhase
{
    /// <summary>尚未开始训练。</summary>
    Idle,

    /// <summary>正在生成仅含正常图片的数据集。</summary>
    PreparingDataset,

    /// <summary>正在检查或安装独立 Python 环境。</summary>
    PreparingEnvironment,

    /// <summary>正在训练异常检测模型。</summary>
    Training,

    /// <summary>正在导出 ONNX 模型。</summary>
    Exporting,

    /// <summary>正在执行本次训练模型与 ONNX 一致性门禁。</summary>
    ValidatingParity,

    /// <summary>模型已完成并通过一致性门禁。</summary>
    Complete,

    /// <summary>训练或一致性验证失败。</summary>
    Failed,

    /// <summary>用户取消了训练。</summary>
    Cancelled,

    /// <summary>模型已完整导出，但校准未通过；保留下载，等待用户自行验证。</summary>
    CalibrationFailed,
}
