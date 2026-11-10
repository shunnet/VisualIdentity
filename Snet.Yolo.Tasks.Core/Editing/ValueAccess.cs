namespace Snet.Yolo.Tasks.Core.Editing;

using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;

/// <summary>行 value 读写辅助（percent JSON）。</summary>
public static class ValueAccess
{
    /// <summary>读字符串数组字段（如 rectanglelabels）。</summary>
    public static List<string> GetStringList(JsonObject value, string fieldName)
        => value[fieldName]?.AsArray().Select(item => item?.GetValue<string>() ?? string.Empty).Where(s => s.Length > 0).ToList() ?? new List<string>();

    /// <summary>写字符串数组字段。</summary>
    public static void SetStringList(JsonObject value, string fieldName, IEnumerable<string> items)
    {
        var array = new JsonArray();
        foreach (var item in items)
        {
            array.Add(item);
        }

        value[fieldName] = array;
    }

    /// <summary>读 double 字段（兼容 int/long/decimal 等数值类型）。</summary>
    public static double GetDouble(JsonObject value, string name)
    {
        if (value[name] is not JsonValue node)
        {
            return 0d;
        }

        if (node.TryGetValue<int>(out var intValue)) { return intValue; }
        if (node.TryGetValue<double>(out var doubleValue)) { return doubleValue; }
        if (node.TryGetValue<long>(out var longValue)) { return longValue; }
        if (node.TryGetValue<decimal>(out var decimalValue)) { return (double)decimalValue; }
        return 0d;
    }

    /// <summary>写 double 字段。</summary>
    public static void SetDouble(JsonObject value, string name, double number) => value[name] = number;
}
