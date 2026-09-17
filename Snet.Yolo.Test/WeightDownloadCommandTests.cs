using Snet.Yolo.Tasks.Core.Training;
using Xunit;

namespace Snet.Yolo.Test;

/// <summary>
/// 预训练权重下载命令：企业代理拦截 github.com 时（curl 60），
/// 程序要按系统给出一条可直接复制的命令，并且带上用户配置的代理 / CA 参数。
/// </summary>
public sealed class WeightDownloadCommandTests
{
    private const string Url26x = "https://github.com/ultralytics/assets/releases/latest/download/yolo26x.pt";

    [Fact]
    public void Build_Linux_UsesCurlWithCreateDirs()
    {
        var command = WeightDownloadCommand.Build(OsKind.Linux, "yolo26x.pt", "/home/ys/tasks/train/weights");

        Assert.Equal($"curl -fL --create-dirs -o '/home/ys/tasks/train/weights/yolo26x.pt' '{Url26x}'", command);
    }

    [Fact]
    public void Build_Windows_UsesCurlExeAndBackslashes()
    {
        var command = WeightDownloadCommand.Build(OsKind.Windows, "yolo26x.pt", @"C:\Snet\app\train\weights");

        Assert.Equal($"curl.exe -fL --create-dirs -o \"C:\\Snet\\app\\train\\weights\\yolo26x.pt\" \"{Url26x}\"", command);
    }

    [Fact]
    public void Build_IncludesConfiguredCaBundleAndProxy()
    {
        // 代理做 HTTPS 拦截时，缺 --cacert 就是 curl 60 的根因：命令里必须带上
        var command = WeightDownloadCommand.Build(
            OsKind.Linux, "yolo26x.pt", "/opt/app/train/weights",
            proxy: "http://proxy.corp:8080", caBundle: "/etc/ssl/certs/corp-ca.crt");

        Assert.Equal(
            $"curl -fL --create-dirs --cacert '/etc/ssl/certs/corp-ca.crt' -x 'http://proxy.corp:8080' -o '/opt/app/train/weights/yolo26x.pt' '{Url26x}'",
            command);
    }

    [Fact]
    public void Build_QuotesPathsWithSpaces()
    {
        var linux = WeightDownloadCommand.Build(OsKind.Linux, "yolo26n.pt", "/home/ys/my tasks/train/weights");
        var windows = WeightDownloadCommand.Build(OsKind.Windows, "yolo26n.pt", @"C:\Program Files\Snet\train\weights");

        Assert.Contains("'/home/ys/my tasks/train/weights/yolo26n.pt'", linux, StringComparison.Ordinal);
        Assert.Contains("\"C:\\Program Files\\Snet\\train\\weights\\yolo26n.pt\"", windows, StringComparison.Ordinal);
    }

    [Fact]
    public void Build_NormalizesSeparatorsForTargetPlatform()
    {
        // 传入混合分隔符也要拼成目标系统的形式
        Assert.Equal("/data/app/train/weights/yolo11n.pt", WeightDownloadCommand.CombineTarget(OsKind.Linux, @"\data\app\train\weights", "yolo11n.pt"));
        Assert.Equal(@"C:\app\train\weights\yolo11n.pt", WeightDownloadCommand.CombineTarget(OsKind.Windows, "C:/app/train/weights", "yolo11n.pt"));
        Assert.Equal("/data/app/train/weights/yolo11n.pt", WeightDownloadCommand.CombineTarget(OsKind.Linux, "/data/app/train/weights/", "yolo11n.pt"));
    }

    [Fact]
    public void Url_UsesLatestReleaseAliasSoVersionNeedNotBeKnown()
    {
        Assert.Equal(Url26x, WeightDownloadCommand.Url("yolo26x.pt"));
        Assert.Contains("/releases/latest/download/", WeightDownloadCommand.Url("yolo11s.pt"), StringComparison.Ordinal);
    }
}
