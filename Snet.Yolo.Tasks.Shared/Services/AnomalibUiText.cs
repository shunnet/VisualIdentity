namespace Snet.Yolo.Tasks.Services;

using Snet.Yolo.Server.Anomalib;

/// <summary>将 Anomalib 后台阶段映射到页面中英文资源键，不改变后台日志内容。</summary>
internal static class AnomalibUiText
{
    /// <summary>返回阶段短标签的资源键。</summary>
    public static string PhaseKey(AnomalibTrainingPhase phase) => phase switch
    {
        AnomalibTrainingPhase.PreparingDataset => "AnomalibStepDataset",
        AnomalibTrainingPhase.PreparingEnvironment => "AnomalibStepEnvironment",
        AnomalibTrainingPhase.Training => "AnomalibStepTraining",
        AnomalibTrainingPhase.Exporting => "AnomalibStepExport",
        AnomalibTrainingPhase.ValidatingParity => "AnomalibStepParity",
        AnomalibTrainingPhase.Complete => "AnomalibStepComplete",
        AnomalibTrainingPhase.Failed => "AnomalibStepFailed",
        AnomalibTrainingPhase.Cancelled => "AnomalibStepCancelled",
        _ => "AnomalibNotStarted",
    };

    /// <summary>返回阶段说明的资源键。</summary>
    public static string PhaseMessageKey(AnomalibTrainingPhase phase) => phase switch
    {
        AnomalibTrainingPhase.PreparingDataset => "AnomalibMessageDataset",
        AnomalibTrainingPhase.PreparingEnvironment => "AnomalibMessageEnvironment",
        AnomalibTrainingPhase.Training => "AnomalibMessageTraining",
        AnomalibTrainingPhase.Exporting => "AnomalibMessageExport",
        AnomalibTrainingPhase.ValidatingParity => "AnomalibMessageParity",
        AnomalibTrainingPhase.Complete => "AnomalibMessageComplete",
        AnomalibTrainingPhase.Failed => "AnomalibMessageFailed",
        AnomalibTrainingPhase.Cancelled => "AnomalibMessageCancelled",
        _ => "AnomalibNotStartedMessage",
    };
}
