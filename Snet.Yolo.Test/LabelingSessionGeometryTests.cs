using Snet.Yolo.Tasks.Core.Editing;
using Snet.Yolo.Tasks.Core.Models;
using Xunit;

namespace Snet.Yolo.Test;

/// <summary>覆盖标注编辑器各几何工具的坐标换算、旋转保持和图像边界行为。</summary>
public sealed class LabelingSessionGeometryTests
{
    private const string LabelConfig = """
        <View>
          <Image name="image" value="$image" />
          <RectangleLabels name="rect" toName="image"><Label value="参数" /></RectangleLabels>
          <PolygonLabels name="polygon" toName="image"><Label value="参数" /></PolygonLabels>
          <KeyPointLabels name="point" toName="image"><Label value="参数" /></KeyPointLabels>
          <EllipseLabels name="ellipse" toName="image"><Label value="参数" /></EllipseLabels>
          <BrushLabels name="brush" toName="image"><Label value="参数" /></BrushLabels>
        </View>
        """;

    /// <summary>详情面板使用百分比更新时不应被再次当成像素缩放，且移动后保持旋转。</summary>
    [Fact]
    public void RectanglePercentUpdate_RoundTripsAndPreservesRotation()
    {
        var session = CreateSession();
        var row = session.AddRectangle(new PixelRect(500, 125, 125, 125), "参数");

        session.UpdateRectanglePercentGeometry(row.Id!, 54.25, 25, 12.5, 23.75, 60);

        Assert.Equal(54.25, ValueAccess.GetDouble(row.Value!, "x"), 6);
        Assert.Equal(25, ValueAccess.GetDouble(row.Value!, "y"), 6);
        Assert.Equal(12.5, ValueAccess.GetDouble(row.Value!, "width"), 6);
        Assert.Equal(23.75, ValueAccess.GetDouble(row.Value!, "height"), 6);
        Assert.Equal(60, ValueAccess.GetDouble(row.Value!, "rotation"), 6);
        AssertRect(session.GetPixelRect(row), 542.5, 125, 125, 118.75);

        session.MoveShape(row.Id!, 10, 5);

        Assert.Equal(60, ValueAccess.GetDouble(row.Value!, "rotation"), 6);
        AssertRect(session.GetPixelRect(row), 552.5, 130, 125, 118.75);
    }

    /// <summary>椭圆缩放应分别保存横纵半径，不应强制变成圆，并保持已有旋转角度。</summary>
    [Fact]
    public void EllipseResize_PreservesIndependentRadiiAndRotation()
    {
        var session = CreateSession();
        var row = session.AddEllipse(500, 250, 200, 75, "参数");
        ValueAccess.SetDouble(row.Value!, "rotation", 30);

        session.ResizeShape(row.Id!, 200, 100, 400, 150);

        Assert.Equal(40, ValueAccess.GetDouble(row.Value!, "x"), 6);
        Assert.Equal(35, ValueAccess.GetDouble(row.Value!, "y"), 6);
        Assert.Equal(20, ValueAccess.GetDouble(row.Value!, "radiusX"), 6);
        Assert.Equal(15, ValueAccess.GetDouble(row.Value!, "radiusY"), 6);
        Assert.Equal(30, ValueAccess.GetDouble(row.Value!, "rotation"), 6);
    }

    /// <summary>矩形、多边形、关键点、椭圆和笔刷平移到边界时均不得越界或改变自身形状。</summary>
    [Fact]
    public void EveryGeometryTool_MoveStaysInsideImage()
    {
        var session = CreateSession();
        var rectangle = session.AddRectangle(new PixelRect(100, 100, 200, 100), "参数");
        var polygon = session.AddPolygon([100d, 200d, 180d], [100d, 100d, 180d], "参数");
        var keyPoint = session.AddKeyPoint(100, 100, "参数");
        var ellipse = session.AddEllipse(200, 150, 50, 25, "参数");
        var brush = session.AddBrushMask([100d, 120d, 140d], [100d, 130d, 110d], 12, "参数");
        var brushRleBefore = brush.Value!["rle"]!.ToJsonString();

        foreach (var row in new[] { rectangle, polygon, keyPoint, ellipse, brush })
        {
            session.MoveShape(row.Id!, -10_000, -10_000);
        }

        AssertRect(session.GetPixelRect(rectangle), 0, 0, 200, 100);
        Assert.All(session.GetPolygonPx(polygon).X, coordinate => Assert.InRange(coordinate, 0, 1000));
        Assert.All(session.GetPolygonPx(polygon).Y, coordinate => Assert.InRange(coordinate, 0, 500));
        Assert.Equal(0, ValueAccess.GetDouble(keyPoint.Value!, "x"), 6);
        Assert.Equal(0, ValueAccess.GetDouble(keyPoint.Value!, "y"), 6);
        Assert.Equal(ValueAccess.GetDouble(ellipse.Value!, "radiusX"), ValueAccess.GetDouble(ellipse.Value!, "x"), 6);
        Assert.Equal(ValueAccess.GetDouble(ellipse.Value!, "radiusY"), ValueAccess.GetDouble(ellipse.Value!, "y"), 6);
        Assert.NotEqual(brushRleBefore, brush.Value!["rle"]!.ToJsonString());
        Assert.Equal(0, brush.Value!["pointxs"]![0]!.GetValue<double>(), 6);
        Assert.Equal(0, brush.Value!["pointys"]![0]!.GetValue<double>(), 6);
    }

    /// <summary>姿态关键点应自动关联所在的最小目标框，框外关键点保持独立。</summary>
    [Fact]
    public void KeyPoint_AssociatesWithSmallestContainingRectangle()
    {
        var session = CreateSession();
        var outer = session.AddRectangle(new PixelRect(50, 50, 400, 300), "参数");
        var inner = session.AddRectangle(new PixelRect(100, 100, 100, 80), "参数");

        var nested = session.AddKeyPoint(150, 140, "参数");
        var independent = session.AddKeyPoint(900, 700, "参数");

        Assert.Equal(inner.Id, nested.ParentId);
        Assert.NotEqual(outer.Id, nested.ParentId);
        Assert.Null(independent.ParentId);
    }

    /// <summary>创建带全部几何控件且原图为 1000×500 的独立编辑会话。</summary>
    private static LabelingSession CreateSession()
    {
        var session = new LabelingSession(LabelConfig, new AnnotationTask());
        session.SetImageOriginalSize(1000, 500);
        return session;
    }

    /// <summary>按容差验证像素矩形。</summary>
    private static void AssertRect(PixelRect actual, double x, double y, double width, double height)
    {
        Assert.Equal(x, actual.X, 6);
        Assert.Equal(y, actual.Y, 6);
        Assert.Equal(width, actual.Width, 6);
        Assert.Equal(height, actual.Height, 6);
    }
}
