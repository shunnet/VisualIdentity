using System.Text;
using System.Xml.Linq;
using Snet.Yolo.Tasks.Core.Config.Templates;
using Snet.Yolo.Tasks.Core.Serialization.Import;
using Xunit;

namespace Snet.Yolo.Test;

/// <summary>
/// 反复往同一个工程里导入 YOLO ZIP 时的类别合并语义（这是"能不能一直往项目里追加标注"的关键）：
///   1. 同名类别（忽略大小写）必须复用工程里已有的标签，不能重复新增；
///   2. ZIP 里新出现的类名才追加；
///   3. 返回的下标 → 名称映射必须把标注下标翻译成工程里的标签名（顺序不同也要对）；
///   4. 同一份包重复导入要保持幂等（标签不重复、颜色仍互不相同）。
/// </summary>
public sealed class YoloLabelMergeTests
{
    private static string Template()
        => ProjectTemplates.Find("object-detection-with-bounding-boxes")?.ConfigXml
           ?? throw new InvalidOperationException("缺少目标检测模板");

    /// <summary>取出工程配置里矩形控件的标签名（顺序即平台界面顺序）。</summary>
    private static IReadOnlyList<string> Labels(string xml)
        => XDocument.Parse(xml).Descendants()
            .Where(element => element.Name.LocalName == "RectangleLabels")
            .SelectMany(rectangle => rectangle.Elements().Where(element => element.Name.LocalName == "Label"))
            .Select(element => element.Attribute("value")?.Value ?? string.Empty)
            .ToList();

    private static IReadOnlyList<string> Colors(string xml)
        => XDocument.Parse(xml).Descendants()
            .Where(element => element.Name.LocalName is "Label" or "Choice")
            .Select(element => element.Attribute("background")?.Value ?? string.Empty)
            .ToList();

    [Fact]
    public void SameNameReusesLabel_NewNameIsAppended()
    {
        // 检测模板自带 Airplane / Car 两个默认标签，导入是在它们之外做增量
        var first = YoloWithImagesImporter.MergeLabels(Template(), new[] { "虫茧", "烟叶" });
        Assert.Equal(new[] { "Airplane", "Car", "虫茧", "烟叶" }, Labels(first.Xml));
        Assert.Equal(new[] { "虫茧", "烟叶" }, first.ClassNames);

        // 第二包：顺序打乱 + 一个新类名
        var second = YoloWithImagesImporter.MergeLabels(first.Xml, new[] { "烟叶", "塑料", "虫茧" });

        Assert.Equal(new[] { "Airplane", "Car", "虫茧", "烟叶", "塑料" }, Labels(second.Xml));   // 只多出"塑料"
        Assert.Equal(new[] { "烟叶", "塑料", "虫茧" }, second.ClassNames);        // 下标按本包顺序映射
    }

    [Fact]
    public void MatchingIsCaseInsensitive_AndResolvesToExistingSpelling()
    {
        var first = YoloWithImagesImporter.MergeLabels(Template(), new[] { "sponge" });

        // 第二包写成 Sponge：应当复用工程里已有的 sponge，而不是新增一个"S ponge"式的重复标签
        var second = YoloWithImagesImporter.MergeLabels(first.Xml, new[] { "SPONGE", "虫茧" });

        Assert.Equal(new[] { "Airplane", "Car", "sponge", "虫茧" }, Labels(second.Xml));
        Assert.Equal(new[] { "sponge", "虫茧" }, second.ClassNames);
    }

    [Fact]
    public void ReimportingSameClassesIsIdempotent()
    {
        var first = YoloWithImagesImporter.MergeLabels(Template(), new[] { "青叶", "梗", "纸" });
        var again = YoloWithImagesImporter.MergeLabels(first.Xml, new[] { "青叶", "梗", "纸" });

        Assert.Equal(Labels(first.Xml), Labels(again.Xml));                       // 没有重复标签
        Assert.Equal(Labels(again.Xml).Distinct(StringComparer.OrdinalIgnoreCase).Count(), Labels(again.Xml).Count);
        Assert.Equal(again.ClassNames, first.ClassNames);
    }

    [Fact]
    public void EveryLabelKeepsADistinctColor_AcrossManyImports()
    {
        var xml = Template();
        foreach (var batch in new[] { new[] { "青叶", "梗" }, new[] { "纸", "麻绳" }, new[] { "青叶切丝", "sponge" }, new[] { "烟茎", "虫茧", "塑料" } })
        {
            xml = YoloWithImagesImporter.MergeLabels(xml, batch).Xml;
        }

        var labels = Labels(xml);
        var colors = Colors(xml);
        Assert.Equal(11, labels.Count);   // 模板自带 2 个 + 导入 9 个
        Assert.Equal(colors.Count, colors.Distinct(StringComparer.OrdinalIgnoreCase).Count());   // 颜色互不重复
        Assert.DoesNotContain(colors, color => string.IsNullOrWhiteSpace(color));
    }

    [Fact]
    public void ProjectWithDuplicateLabelNames_StillImportsInsteadOfThrowing()
    {
        // 手工在标签编辑器里加出重名标签（或历史数据留下重名）时，
        // 之后的每一次 ZIP 上传都不该因为"同名两条"而失败——复用第一条即可。
        // 真的注入一个与模板自带 "Car" 仅大小写不同的重名标签
        var document = XDocument.Parse(Template());
        var rectangle = document.Descendants().First(element => element.Name.LocalName == "RectangleLabels");
        rectangle.Add(new XElement("Label", new XAttribute("value", "car"), new XAttribute("background", "#123456")));
        var duplicated = document.ToString();
        Assert.Equal(2, Labels(duplicated).Count(name => name.Equals("car", StringComparison.OrdinalIgnoreCase)));

        var merged = YoloWithImagesImporter.MergeLabels(duplicated, new[] { "CAR", "虫茧" });

        Assert.Equal(new[] { "Car", "虫茧" }, merged.ClassNames);                 // 命中已有标签（保留先出现的写法）
        Assert.Equal(2, Labels(merged.Xml).Count(name => name.Equals("car", StringComparison.OrdinalIgnoreCase)));   // 不新增第三个
    }
    [Fact]
    public void TenClasses_AllMapInOrder()
    {
        // 现场数据现在有 10 个类别（0~9），一次全导入也要能正确映射
        var names = Enumerable.Range(0, 10).Select(index => "类别" + index).ToArray();
        var merged = YoloWithImagesImporter.MergeLabels(Template(), names);

        Assert.Equal(names, merged.ClassNames);
        Assert.Equal(new[] { "Airplane", "Car" }.Concat(names), Labels(merged.Xml));
    }
}
