using Snet.Yolo.Server;

namespace Snet.Yolo.Tasks.Services;

/// <summary>生成不会越出存储根目录的用户名文件夹。</summary>
public static class UserStoragePath
{
    /// <summary>安全用户名保持原样；其他用户名使用可读前缀和稳定摘要，避免碰撞。</summary>
    public static string Segment(string userName) => OwnerStoragePath.Segment(userName);
}
