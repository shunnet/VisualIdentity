using Snet.Yolo.Tasks.Core.Config;
using Snet.Yolo.Tasks.Core.Editing;
using Snet.Yolo.Tasks.Core.Models;
using Xunit;

namespace Snet.Yolo.Test;

/// <summary>标注页右侧区域选中后，左侧标签栏必须同步展示并高亮对应标签的规则。</summary>
public sealed class LabelSelectionResolverTests
{
    private static readonly IReadOnlyList<LabelOptionInfo> Labels =
    [
        new() { Value = "虫茧" },
        new() { Value = "烟叶" },
        new() { Value = "金属" },
    ];

    /// <summary>每种几何区域都映射到自己的控件，文本类区域不映射。</summary>
    [Theory]
    [InlineData(RegionType.RectangleLabels, ControlTagKind.RectangleLabels)]
    [InlineData(RegionType.PolygonLabels, ControlTagKind.PolygonLabels)]
    [InlineData(RegionType.KeyPointLabels, ControlTagKind.KeyPointLabels)]
    [InlineData(RegionType.EllipseLabels, ControlTagKind.EllipseLabels)]
    [InlineData(RegionType.BrushLabels, ControlTagKind.BrushLabels)]
    public void KindOf_MapsGeometryRegionTypes(string regionType, ControlTagKind expected)
    {
        Assert.Equal(expected, LabelSelectionResolver.KindOf(regionType));
    }

    /// <summary>未知、文本类或空类型没有对应的几何控件。</summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(RegionType.TextArea)]
    [InlineData("unknown")]
    public void KindOf_ReturnsNullForNonGeometryTypes(string? regionType)
    {
        Assert.Null(LabelSelectionResolver.KindOf(regionType));
    }

    /// <summary>区域标签按值精确匹配到左侧下标。</summary>
    [Theory]
    [InlineData("虫茧", 0)]
    [InlineData("烟叶", 1)]
    [InlineData("金属", 2)]
    public void IndexOf_FindsLabelPosition(string label, int expected)
    {
        Assert.Equal(expected, LabelSelectionResolver.IndexOf(Labels, label));
    }

    /// <summary>区域没有标签、标签已不在配置中或大小写不同，都不应移动左侧选中项。</summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("不存在")]
    [InlineData("金属 ")]
    public void IndexOf_ReturnsMinusOneWhenNothingMatches(string? label)
    {
        Assert.Equal(-1, LabelSelectionResolver.IndexOf(Labels, label));
    }

    /// <summary>选中多边形区域时，即使工程同时配置了矩形控件，左侧也必须切到多边形的标签。</summary>
    [Fact]
    public void ResolveKind_PrefersSelectedRegionControl()
    {
        var kind = LabelSelectionResolver.ResolveKind(RegionType.PolygonLabels, _ => true);
        Assert.Equal(ControlTagKind.PolygonLabels, kind);
    }

    /// <summary>没有选中区域时，只配置了多边形标签的分割工程也应展示标签，而不是提示先添加标签。</summary>
    [Fact]
    public void ResolveKind_FallsBackToFirstControlWithLabels()
    {
        var kind = LabelSelectionResolver.ResolveKind(null, candidate => candidate == ControlTagKind.PolygonLabels);
        Assert.Equal(ControlTagKind.PolygonLabels, kind);
    }

    /// <summary>矩形控件有标签时仍是默认控件，保持与旧版一致的展示。</summary>
    [Fact]
    public void ResolveKind_KeepsRectangleWhenAvailable()
    {
        var kind = LabelSelectionResolver.ResolveKind(null, _ => true);
        Assert.Equal(ControlTagKind.RectangleLabels, kind);
    }

    /// <summary>所有控件都没有标签时回到矩形控件，与旧行为一致。</summary>
    [Fact]
    public void ResolveKind_DefaultsToRectangleWithoutLabels()
    {
        var kind = LabelSelectionResolver.ResolveKind(null, _ => false);
        Assert.Equal(ControlTagKind.RectangleLabels, kind);
    }
}
