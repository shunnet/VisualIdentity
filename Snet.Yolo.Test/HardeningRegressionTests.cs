using Snet.Yolo.Server;
using Snet.Yolo.Tasks.Core.Localization;
using Snet.Yolo.Tasks.Core.Training;
using Snet.Yolo.Tasks.Services;
using Xunit;

namespace Snet.Yolo.Test;

/// <summary>Regression tests for identity normalization, localization resources, and strict log parsing.</summary>
public sealed class HardeningRegressionTests
{
    /// <summary>Compatibility-equivalent, case-only username variants must share one identity and directory.</summary>
    [Fact]
    public void UserNameNormalization_PreventsCaseAndUnicodeStorageAliases()
    {
        Assert.Equal(UserNameNormalizer.Normalize(" User "), UserNameNormalizer.Normalize("user"));
        Assert.Equal(UserNameNormalizer.Normalize("Ａlice"), UserNameNormalizer.Normalize("alice"));
        Assert.Equal(UserStoragePath.Segment(" User "), UserStoragePath.Segment("user"));
        Assert.Equal(UserStoragePath.Segment("Ａlice"), UserStoragePath.Segment("alice"));
    }

    /// <summary>The neutral resource set must enumerate actual keys rather than dictionary entries.</summary>
    [Fact]
    public void SupportedLanguageKeys_ContainsKnownResources()
    {
        Assert.NotEmpty(LanguageManager.SupportedKeys);
        Assert.Contains(LanguageManager.SupportedKeys, key => !string.IsNullOrWhiteSpace(key));
    }

    /// <summary>A class name ending in "all" is not the Ultralytics aggregate metrics row.</summary>
    [Fact]
    public void MetricsParser_RejectsClassNamesEndingInAll()
    {
        Assert.Null(YoloOutputParser.ParseMetrics("football 12 18 0.91 0.82 0.73 0.64"));
        Assert.NotNull(YoloOutputParser.ParseMetrics("all 12 18 0.91 0.82 0.73 0.64"));
    }
}
