namespace Snet.Yolo.Tasks.Core.Training;

/// <summary>Kind of environment-setup operation.</summary>
public enum SetupStepKind
{
    /// <summary>Informational step.</summary>
    Info,
    /// <summary>Create or recreate the Python virtual environment.</summary>
    RecreateVenv,
    /// <summary>Install PyTorch packages.</summary>
    PipInstallTorch,
    /// <summary>Install Ultralytics.</summary>
    PipInstallYolo,
    /// <summary>安装固定版本的 Anomalib 与 ONNX 导出依赖。</summary>
    PipInstallAnomalib,
    /// <summary>Install the Visual C++ runtime.</summary>
    InstallVcRedist,
}
