namespace Snet.Yolo.Tasks.Core.Training;

/// <summary>大体积依赖安装的 pip 缓存、超时和断点续传参数。</summary>
public static class PipInstallOptions
{
    /// <summary>升级当前私有 Python 环境的 pip，兼容系统自带的旧版 pip。</summary>
    public static SetupStep UpgradeStep(string python, string cacheDirectory)
        => new(SetupStepKind.PipUpgrade, "升级应用私有环境的 pip",
            new SetupCommand(python, ["-m", "pip", "install", .. DownloadArguments(cacheDirectory), "pip>=26.2.1,<27"], true));

    /// <summary>基础网络参数。引导 pip 升级时也兼容不识别续传选项的旧 pip。</summary>
    public static string[] DownloadArguments(string cacheDirectory)
        => ["--disable-pip-version-check", "--cache-dir", cacheDirectory, "--timeout", "60"];

    /// <summary>给已升级的 pip 使用，支持 HTTP Range 的服务端可从现有偏移续传。</summary>
    public static string[] ResumableArguments(string cacheDirectory)
        => [.. DownloadArguments(cacheDirectory), "--resume-retries", "8"];
}
