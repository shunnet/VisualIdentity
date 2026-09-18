using System.Text.RegularExpressions;
using Xunit;

namespace Snet.Yolo.Test;

/// <summary>
/// 多语言资源一致性：中英两份 resx 的键必须完全一致，且代码里用到的字面量键都必须在两份里存在。
///
/// 背景：项目详情页曾经把"标注/分类进度完成"的文案误用成训练页的 <c>StatusComplete</c>（"训练完成"），
/// 结果一个从没训练过的分类工程显示"9 / 9 训练完成"。键写错/漏翻译这类问题很难在界面上逐个发现，
/// 交给测试守住。
/// </summary>
public sealed class LocalizationConsistencyTests
{
    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !Directory.Exists(Path.Combine(directory.FullName, "Snet.Yolo.Tasks.Shared")))
        {
            directory = directory.Parent;
        }
        return directory?.FullName ?? throw new DirectoryNotFoundException("找不到仓库根目录（含 Snet.Yolo.Tasks.Shared 的目录）。");
    }

    private static Dictionary<string, string> LoadKeys(string path)
    {
        var text = File.ReadAllText(path);
        var keys = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (Match match in Regex.Matches(text, "<data name=\"([^\"]+)\"[^>]*>\\s*<value>(.*?)</value>", RegexOptions.Singleline))
        {
            keys[match.Groups[1].Value] = match.Groups[2].Value.Trim();
        }
        return keys;
    }

    private static string ResxPath(string name) => Path.Combine(RepositoryRoot(), "Snet.Yolo.Tasks.Core", "Localization", name);

    [Fact]
    public void BothLanguages_HaveExactlyTheSameKeys()
    {
        var english = LoadKeys(ResxPath("AppResource.resx"));
        var chinese = LoadKeys(ResxPath("AppResource.zh-CN.resx"));

        Assert.NotEmpty(english);
        var missingInChinese = english.Keys.Except(chinese.Keys, StringComparer.Ordinal).OrderBy(key => key, StringComparer.Ordinal).ToList();
        var missingInEnglish = chinese.Keys.Except(english.Keys, StringComparer.Ordinal).OrderBy(key => key, StringComparer.Ordinal).ToList();

        Assert.True(missingInChinese.Count == 0, "中文资源缺少：" + string.Join("、", missingInChinese));
        Assert.True(missingInEnglish.Count == 0, "英文资源缺少：" + string.Join("、", missingInEnglish));
    }

    [Fact]
    public void NoResourceValue_IsEmptyOrDuplicatedAcrossKeys()
    {
        foreach (var resource in new[] { LoadKeys(ResxPath("AppResource.resx")), LoadKeys(ResxPath("AppResource.zh-CN.resx")) })
        {
            Assert.DoesNotContain(resource, pair => string.IsNullOrWhiteSpace(pair.Value));
        }
    }

    [Fact]
    public void EveryLiteralKeyUsedInCode_ExistsInBothLanguages()
    {
        var root = RepositoryRoot();
        var english = LoadKeys(ResxPath("AppResource.resx"));
        var chinese = LoadKeys(ResxPath("AppResource.zh-CN.resx"));
        // 只认真正的字面量键：Translate("X") / L("X") / <LangText Key="X" />（变量参数一律跳过）
        var pattern = new Regex("(?:Translate\\(|\\bL\\()\\s*\"([A-Za-z][A-Za-z0-9_]*)\"|<LangText[^>]*Key=\"([A-Za-z][A-Za-z0-9_]*)\"", RegexOptions.Compiled);
        var unknown = new SortedSet<string>(StringComparer.Ordinal);

        foreach (var file in Directory.EnumerateFiles(Path.Combine(root, "Snet.Yolo.Tasks.Shared"), "*.*", SearchOption.AllDirectories))
        {
            var extension = Path.GetExtension(file);
            if (extension is not (".razor" or ".cs")) { continue; }
            if (file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}") || file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}")) { continue; }
            var text = File.ReadAllText(file);
            foreach (Match match in pattern.Matches(text))
            {
                var key = match.Groups[1].Success ? match.Groups[1].Value : match.Groups[2].Value;
                if (!english.ContainsKey(key) || !chinese.ContainsKey(key)) { unknown.Add(key); }
            }
        }

        Assert.True(unknown.Count == 0, "代码里用到但资源里没有的键：" + string.Join("、", unknown));
    }

    [Fact]
    public void ProgressCaption_DoesNotBorrowTheTrainingStatusText()
    {
        // 进度行的文案不能再用训练页的 StatusComplete（历史 bug：未训练的分类工程显示"训练完成"）
        var page = Path.Combine(RepositoryRoot(), "Snet.Yolo.Tasks.Shared", "Components", "Pages", "ProjectDetails.razor");
        var text = File.ReadAllText(page);

        Assert.Contains("ProgressAllDone", text, StringComparison.Ordinal);
        Assert.DoesNotContain("Translate(\"StatusComplete\")", text, StringComparison.Ordinal);
    }
}
