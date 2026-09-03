
namespace Snet.Yolo.Tasks.Core.Editing;

using System.Collections.Generic;

/// <summary>
/// 文本标注 span 工具：按空白分词，并把“选中 token 区间”映射为字符偏移（start 含 / end 排他），
/// 与 CoNLL 导出、Labels(span) 语义保持一致。
/// </summary>
public static class TextSpanUtil
{
    /// <summary>按空白切分文本（保留字符偏移，end 为排他）。</summary>
    public static IReadOnlyList<(string Text, int Start, int End)> Tokenize(string text)
    {
        var tokens = new List<(string, int, int)>();
        var index = 0;
        while (index < text.Length)
        {
            while (index < text.Length && char.IsWhiteSpace(text[index])) { index++; }
            if (index >= text.Length) { break; }
            var start = index;
            while (index < text.Length && !char.IsWhiteSpace(text[index])) { index++; }
            tokens.Add((text[start..index], start, index));
        }
        return tokens;
    }

    /// <summary>由首尾 token 下标构造 span（覆盖 start..end 的文本）。</summary>
    public static (int Start, int End, string Text) BuildSpan(string text, int firstToken, int lastToken)
    {
        var tokens = Tokenize(text);
        if (tokens.Count == 0 || firstToken < 0 || lastToken >= tokens.Count || firstToken > lastToken)
        {
            return (0, 0, string.Empty);
        }
        var start = tokens[firstToken].Start;
        var end = tokens[lastToken].End;
        return (start, end, text[start..end]);
    }
}
