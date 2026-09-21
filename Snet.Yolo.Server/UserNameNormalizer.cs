using System.Text;

namespace Snet.Yolo.Server;

/// <summary>Produces the canonical identity used for username comparison and storage isolation.</summary>
public static class UserNameNormalizer
{
    /// <summary>Applies compatibility Unicode normalization, trimming and invariant case folding.</summary>
    /// <param name="userName">User-supplied or persisted username.</param>
    /// <returns>A stable canonical username.</returns>
    public static string Normalize(string userName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userName);
        return userName.Trim().Normalize(NormalizationForm.FormKC).ToLowerInvariant();
    }
}
