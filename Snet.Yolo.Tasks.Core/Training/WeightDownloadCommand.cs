namespace Snet.Yolo.Tasks.Core.Training;

using System.Collections.Generic;

/// <summary>
/// 生成"把预训练权重直接下载到指定目录"的单行命令。
///
/// 背景：企业网络/代理做 HTTPS 拦截时，Ultralytics 自己下载 github.com 上的权重会因证书校验
/// 失败直接终止训练（curl 60 / CERTIFICATE_VERIFY_FAILED）。与其让用户自己拼 URL、找目录、
/// 再想办法让 curl 信任代理的 CA，不如由程序按当前系统直接给出可复制的命令。
/// </summary>
public static class WeightDownloadCommand
{
    /// <summary>权重发布地址模板（<c>latest/download</c> 对每个正式发布都有效，无需推算版本号）。</summary>
    public const string ReleaseUrlTemplate = "https://github.com/ultralytics/assets/releases/latest/download/{0}";

    /// <summary>权重的下载地址。</summary>
    /// <param name="modelName">权重文件名，例如 yolo26x.pt。</param>
    public static string Url(string modelName) => string.Format(ReleaseUrlTemplate, modelName);

    /// <summary>
    /// 生成下载命令：Windows 用 curl.exe（Win10 1803+ 自带），其它平台用 curl。
    /// 配置了代理/CA 时会把 <c>-x</c> / <c>--cacert</c> 一并带上——这正是代理拦截场景下 curl 60 的解法
    /// （不关闭证书校验，而是显式信任企业 CA）。
    /// </summary>
    /// <param name="os">目标操作系统。</param>
    /// <param name="modelName">权重文件名。</param>
    /// <param name="destinationDirectory">下载目标目录（不存在时由 --create-dirs 自动创建）。</param>
    /// <param name="proxy">Training:Proxy 配置的代理地址，可为空。</param>
    /// <param name="caBundle">Training:CaBundle 配置的 CA 证书包路径，可为空。</param>
    public static string Build(OsKind os, string modelName, string destinationDirectory, string? proxy = null, string? caBundle = null)
    {
        var executable = os == OsKind.Windows ? "curl.exe" : "curl";
        var arguments = new List<string> { executable, "-fL", "--create-dirs" };
        if (!string.IsNullOrWhiteSpace(caBundle)) { arguments.Add("--cacert"); arguments.Add(Quote(caBundle.Trim(), os)); }
        if (!string.IsNullOrWhiteSpace(proxy)) { arguments.Add("-x"); arguments.Add(Quote(proxy.Trim(), os)); }
        arguments.Add("-o");
        arguments.Add(Quote(CombineTarget(os, destinationDirectory, modelName), os));
        arguments.Add(Quote(Url(modelName), os));
        return string.Join(' ', arguments);
    }

    /// <summary>权重应落到的完整路径。</summary>
    public static string CombineTarget(OsKind os, string destinationDirectory, string modelName)
    {
        var separator = os == OsKind.Windows ? '\\' : '/';
        var directory = destinationDirectory.Replace('\\', separator).Replace('/', separator).TrimEnd(separator);
        return directory + separator + modelName;
    }

    /// <summary>按目标系统选择引号：Windows 用双引号，类 Unix 用单引号（路径含空格也安全）。</summary>
    private static string Quote(string value, OsKind os)
        => os == OsKind.Windows ? "\"" + value.Replace("\"", "\\\"") + "\"" : "'" + value.Replace("'", "'\\''") + "'";
}
