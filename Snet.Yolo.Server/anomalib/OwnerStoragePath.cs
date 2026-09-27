using System.Security.Cryptography;
using System.Text;
using Snet.Yolo.Server;

namespace Snet.Yolo.Server;

/// <summary>生成不会越出存储根目录的用户名文件夹。</summary>
public static class OwnerStoragePath
{
    private static readonly HashSet<string> WindowsReservedNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "CON", "PRN", "AUX", "NUL", "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9",
    };

    /// <summary>安全用户名保持原样；其他用户名使用可读前缀和稳定摘要，避免碰撞。</summary>
    public static string Segment(string userName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userName);
        var normalized = UserNameNormalizer.Normalize(userName);
        if (normalized.Length <= 80 && normalized is not "." and not ".." && !WindowsReservedNames.Contains(normalized) && normalized.All(character => char.IsLetterOrDigit(character) || character is '-' or '_' or '.'))
        {
            return normalized;
        }
        var readable = new string(normalized.Select(character => char.IsLetterOrDigit(character) || character is '-' or '_' ? character : '_').Take(48).ToArray()).Trim('_');
        if (readable.Length == 0) { readable = "user"; }
        var digest = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(normalized)))[..12].ToLowerInvariant();
        return $"{readable}_{digest}";
    }
}
