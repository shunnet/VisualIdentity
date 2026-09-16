namespace Snet.Yolo.Tasks.Core.Training;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

/// <summary>
/// 预训练权重（yolo*.pt）的本地查找与缓存。
///
/// 背景：Ultralytics 在 model=yolo26n.pt 不存在时会去 github.com 下载；企业网络/代理做 HTTPS
/// 拦截时这一步会因为证书校验失败直接终止训练（curl 60 / CERTIFICATE_VERIFY_FAILED）。
/// 所以先找本地已下载过的权重并复制到训练工作目录，Ultralytics 就不会再联网。
/// </summary>
public static class WeightCache
{
    /// <summary>Ultralytics 自己的权重目录（新版 Ultralytics 的下载目标，也是手动放置的推荐位置之一）。</summary>
    public static string UltralyticsWeightsDirectory()
        => OperatingSystem.IsWindows()
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Ultralytics", "weights")
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config", "Ultralytics", "weights");

    /// <summary>按查找顺序返回候选路径：训练工作目录 → 应用缓存目录 → Ultralytics 权重目录。</summary>
    public static IReadOnlyList<string> Candidates(string cacheDirectory, string workingDirectory, string modelName)
        => new[]
        {
            Path.Combine(workingDirectory, modelName),
            Path.Combine(cacheDirectory, modelName),
            Path.Combine(UltralyticsWeightsDirectory(), modelName),
        };

    /// <summary>返回第一个已存在的权重路径；都没有时返回 null。</summary>
    public static string? Locate(string cacheDirectory, string workingDirectory, string modelName)
        => Candidates(cacheDirectory, workingDirectory, modelName).FirstOrDefault(File.Exists);

    /// <summary>
    /// 把已存在的权重复制进应用缓存目录，供后续工程复用；返回缓存路径，找不到来源时返回 null。
    /// 复制失败（权限/占用）不抛异常，只是无法复用。
    /// </summary>
    public static string? Cache(string cacheDirectory, string workingDirectory, string modelName)
    {
        var source = Locate(cacheDirectory, workingDirectory, modelName);
        if (source is null) { return null; }
        var target = Path.Combine(cacheDirectory, modelName);
        if (string.Equals(Path.GetFullPath(source), Path.GetFullPath(target), StringComparison.Ordinal)) { return target; }
        try
        {
            Directory.CreateDirectory(cacheDirectory);
            File.Copy(source, target, overwrite: true);
            return target;
        }
        catch (IOException) { return null; }
        catch (UnauthorizedAccessException) { return null; }
    }
}
