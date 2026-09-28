using Snet.Yolo.Server.anomalib;

namespace Snet.Yolo.Server;

/// <summary>训练用户目录与算法共享目录隔离。</summary>
public static class TrainingStoragePath
{
    private static readonly HashSet<string> SharedNames = new(StringComparer.OrdinalIgnoreCase)
    {
        ".env", "weights", "statuses", "scripts",
    };

    /// <summary>生成用户目录，避免用户名占用共享环境、权重或状态目录。</summary>
    public static string OwnerDirectory(string algorithmRoot, string owner)
    {
        var segment = OwnerStoragePath.Segment(owner);
        if (SharedNames.Contains(segment)) { segment = OwnerStoragePath.Segment("user:" + owner); }
        return Path.GetFullPath(Path.Combine(algorithmRoot, segment));
    }
}
