using System.Collections;
using System.Globalization;
using System.Resources;
using System.Text.RegularExpressions;
using Snet.Yolo.Tasks.Core.Localization;
using Xunit;

namespace Snet.Yolo.Test;

/// <summary>保证 Anomalib 页面新增的中英文资源键和格式占位符保持一致。</summary>
public sealed class AnomalibLocalizationTests
{
    /// <summary>每一个 Anomalib 资源都必须有英文及简体中文版本。</summary>
    [Fact]
    public void AnomalibResources_HaveMatchingKeysAndPlaceholders()
    {
        var manager = new ResourceManager("Snet.Yolo.Tasks.Core.Localization.AppResource", typeof(LanguageManager).Assembly);
        var english = ReadAnomalibResources(manager.GetResourceSet(CultureInfo.InvariantCulture, true, false)!);
        var chinese = ReadAnomalibResources(manager.GetResourceSet(CultureInfo.GetCultureInfo("zh-CN"), true, false)!);
        Assert.NotEmpty(english);
        Assert.Equal(english.Keys.Order(StringComparer.Ordinal), chinese.Keys.Order(StringComparer.Ordinal));
        foreach (var (key, value) in english)
        {
            Assert.Equal(Placeholders(value), Placeholders(chinese[key]));
        }
    }

    /// <summary>读取指定文化中所有 Anomalib 资源，不释放 ResourceManager 缓存的集合。</summary>
    private static Dictionary<string, string> ReadAnomalibResources(ResourceSet set)
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (DictionaryEntry entry in set)
        {
            if (entry.Key is string key && key.StartsWith("Anomalib", StringComparison.Ordinal))
            {
                values.Add(key, (string)entry.Value!);
            }
        }
        return values;
    }

    /// <summary>提取 string.Format 序号，发现翻译中漏写或错写的参数。</summary>
    private static int[] Placeholders(string value)
        => Regex.Matches(value, @"(?<!\{)\{(\d+)(?:[^}]*)\}(?!\})")
            .Select(match => int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture))
            .Order()
            .ToArray();
}
