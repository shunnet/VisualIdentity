namespace Snet.Yolo.Tasks.Core.Anomalib;

using Snet.Yolo.Server.Anomalib;

using System.Security.Cryptography;
using System.Text;

/// <summary>为仅含正常图片的 Anomalib 数据集创建稳定且无泄漏的训练/校准划分。</summary>
public static class AnomalibDatasetPlanner
{
    /// <summary>启动训练所需的最少唯一正常图片数。</summary>
    public const int MinimumImageCount = 10;

    /// <summary>依据内容摘要和种子稳定划分数据，同内容图片只保留一份。</summary>
    /// <param name="images">已经计算内容摘要的正常图片。</param>
    /// <param name="options">训练选项。</param>
    /// <returns>稳定训练集、校准集和数据集摘要。</returns>
    public static AnomalibDatasetSplit Create(IReadOnlyList<AnomalibImageSource> images, AnomalibTrainingOptions options)
    {
        ArgumentNullException.ThrowIfNull(images);
        ArgumentNullException.ThrowIfNull(options);
        options.Validate();

        var unique = images
            .Where(static image => !string.IsNullOrWhiteSpace(image.SourcePath) && IsSha256(image.ContentSha256))
            .GroupBy(static image => image.ContentSha256, StringComparer.OrdinalIgnoreCase)
            .Select(static group => group.OrderBy(image => image.SourcePath, StringComparer.Ordinal).First())
            .Select(image => new RankedImage(image, Rank(image.ContentSha256, options.RandomSeed)))
            .OrderBy(static image => image.Rank, StringComparer.Ordinal)
            .ThenBy(static image => image.Source.ContentSha256, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (unique.Length < MinimumImageCount)
        {
            throw new InvalidOperationException($"至少需要 {MinimumImageCount} 张内容不同的正常图片，当前只有 {unique.Length} 张。");
        }

        var calibrationCount = Math.Clamp(
            (int)Math.Round(unique.Length * options.CalibrationRatio, MidpointRounding.AwayFromZero),
            1,
            unique.Length - 1);
        var calibration = unique.Take(calibrationCount).Select(static item => item.Source).ToArray();
        var training = unique.Skip(calibrationCount).Select(static item => item.Source).ToArray();
        var datasetHash = ComputeDatasetHash(unique.Select(static item => item.Source.ContentSha256));
        return new AnomalibDatasetSplit(training, calibration, datasetHash);
    }

    /// <summary>判断字符串是否为完整 SHA-256 十六进制摘要。</summary>
    private static bool IsSha256(string value)
        => value.Length == 64 && value.All(static character => char.IsAsciiHexDigit(character));

    /// <summary>由内容摘要与种子计算不可预测但可复现的排序键。</summary>
    private static string Rank(string contentSha256, int seed)
    {
        var bytes = Encoding.UTF8.GetBytes(seed.ToString(System.Globalization.CultureInfo.InvariantCulture) + ":" + contentSha256.ToLowerInvariant());
        return Convert.ToHexStringLower(SHA256.HashData(bytes));
    }

    /// <summary>由全部唯一图片的有序内容摘要计算数据集身份摘要。</summary>
    private static string ComputeDatasetHash(IEnumerable<string> contentHashes)
    {
        var canonical = string.Join('\n', contentHashes.Order(StringComparer.OrdinalIgnoreCase).Select(static hash => hash.ToLowerInvariant()));
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)));
    }

    /// <summary>带稳定排序键的图片。</summary>
    /// <param name="Source">图片来源。</param>
    /// <param name="Rank">稳定排序键。</param>
    private sealed record RankedImage(AnomalibImageSource Source, string Rank);
}
