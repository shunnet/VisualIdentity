namespace Snet.Yolo.Tasks.Core.Training;

/// <summary>One ordered environment-setup step.</summary>
/// <param name="Kind">Operation kind.</param>
/// <param name="Description">User-facing description.</param>
/// <param name="Command">Command to execute.</param>
public sealed record SetupStep(SetupStepKind Kind, string Description, SetupCommand Command)
{
    /// <summary>该步骤是否为可重试的网络 pip 安装。</summary>
    public bool IsNetwork => Command.IsNetwork;
}
