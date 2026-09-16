using System.Text.RegularExpressions;
using Xunit;

namespace Snet.Yolo.Test;

/// <summary>
/// Razor 书写守卫：`文字@if`、`count@Model.Count` 这类"@ 紧跟在字母/数字后面"的写法，
/// Razor 会按邮箱地址的规则当成<b>普通文字</b>处理（不做表达式求值），结果把代码原样渲染到页面上。
/// 编译能通过、浏览器里的"硬编码结构"验证也看不出来，所以这里静态扫描全部 .razor 文件拦住它。
/// </summary>
public sealed class RazorMarkupLintTests
{
    /// <summary>字母/数字/下划线 + @ + 字母 → Razor 视为邮箱式文字，不求值。</summary>
    private static readonly Regex GluedTransition = new(@"[A-Za-z0-9_]@[A-Za-z]", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex RazorComment = new(@"@\*.*?\*@", RegexOptions.Compiled | RegexOptions.Singleline);
    private static readonly Regex ScriptOrStyleBlock = new(@"<(script|style)\b.*?</\1>", RegexOptions.Compiled | RegexOptions.Singleline | RegexOptions.IgnoreCase);
    private static readonly Regex DoubleQuoted = new("\"[^\"]*\"", RegexOptions.Compiled);
    private static readonly Regex SingleQuoted = new("'[^']*'", RegexOptions.Compiled);

    /// <summary>扫描一段 Razor 文本，返回隐患行（行号从 1 开始）与原文。</summary>
    public static IReadOnlyList<(int Line, string Text)> Scan(string razorText)
    {
        var cleaned = ScriptOrStyleBlock.Replace(RazorComment.Replace(razorText, " "), " ");
        var lines = cleaned.Split('\n');
        var hits = new List<(int, string)>();
        for (var i = 0; i < lines.Length; i++)
        {
            // 引号内是属性值/字符串（例如 mailto:a@b.com），不参与判断
            var line = SingleQuoted.Replace(DoubleQuoted.Replace(lines[i], "\"\""), "''");
            if (GluedTransition.IsMatch(line)) { hits.Add((i + 1, lines[i].Trim())); }
        }
        return hits;
    }

    [Theory]
    [InlineData("<small>Tasks@if (_version.Length > 0) { }</small>")]
    [InlineData("<small>Tasks@foreach (var item in items) { }</small>")]
    [InlineData("<span>x@name</span>")]
    [InlineData("<td>@* c *@count@Model.Count</td>")]
    public void Scan_DetectsGluedTransition(string razor)
    {
        Assert.Single(Scan(razor));
    }

    [Theory]
    [InlineData("<small>Tasks @if (_version.Length > 0) { }</small>")]   // 加空格即可正常求值
    [InlineData("<small>Tasks@_version</small>")]                        // @ 后是下划线：正常求值
    [InlineData("<span>@value</span>")]
    [InlineData("<span>@(a + b)</span>")]
    [InlineData("<a href=\"mailto:a@b.com\">mail</a>")]
    [InlineData("<span title=\"v@_version\">x</span>")]
    [InlineData("@* Tasks@if 写在注释里不算 *@")]
    [InlineData("<script>var a = x@y;</script>")]
    public void Scan_AllowsValidMarkup(string razor)
    {
        Assert.Empty(Scan(razor));
    }

    [Fact]
    public void AllRazorFiles_AvoidGluedTransitions()
    {
        var root = FindRepositoryRoot();
        if (root is null) { return; }   // 从打包产物运行时没有源码，跳过
        var files = Directory.EnumerateFiles(root, "*.razor", SearchOption.AllDirectories)
            .Where(path => !path.Contains(Path.DirectorySeparatorChar + "bin" + Path.DirectorySeparatorChar, StringComparison.Ordinal)
                        && !path.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar, StringComparison.Ordinal))
            .ToList();
        Assert.NotEmpty(files);

        var problems = new List<string>();
        foreach (var file in files)
        {
            foreach (var (line, text) in Scan(File.ReadAllText(file)))
            {
                problems.Add(Path.GetRelativePath(root, file) + ":" + line + "  " + text);
            }
        }
        Assert.True(problems.Count == 0,
            "Razor 中 @ 紧跟字母会被当成普通文字原样输出（页面会显示代码），请改成“文字 @if”这类带空格的写法：\n" + string.Join("\n", problems));
    }

    private static string? FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (Directory.Exists(Path.Combine(directory.FullName, "Snet.Yolo.Tasks.Shared"))) { return directory.FullName; }
            directory = directory.Parent;
        }
        return null;
    }
}
