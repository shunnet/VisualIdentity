namespace Snet.Yolo.Tasks.Services;

/// <summary>执行系统命令（抽出来便于单测：测试里用假执行器模拟 apt 安装）。</summary>
public interface ISystemCommandRunner
{
    /// <summary>流式执行命令，把输出逐行回调给调用方。</summary>
    Task<(int ExitCode, string Tail, bool Stalled)> RunStreamingAsync(string file, IReadOnlyList<string> arguments, Action<string> onOutput, TimeSpan stallTimeout, CancellationToken cancellationToken);

    /// <summary>文件是否存在。</summary>
    bool FileExists(string path);

    /// <summary>在 PATH 中查找可执行文件，找不到返回 null。</summary>
    string? FindOnPath(string executableName);
}
