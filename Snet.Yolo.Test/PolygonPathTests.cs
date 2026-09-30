using System.Globalization;
using System.Text.Json.Nodes;
using Snet.Yolo.Tasks.Core.Config;
using Snet.Yolo.Tasks.Core.Editing;
using Snet.Yolo.Tasks.Core.Geometry;
using Snet.Yolo.Tasks.Core.Models;
using Snet.Yolo.Tasks.Core.Serialization;
using Snet.Yolo.Tasks.Core.Serialization.Export;
using Xunit;

namespace Snet.Yolo.Test;

public sealed class PolygonPathTests
{
    private const string Config = """
        <View><Image name="image" value="$image"/><PolygonLabels name="polygon" toName="image"><Label value="area"/></PolygonLabels></View>
        """;
    private static (LabelingSession Session, AnnotationTask Task, ResultRow Row) Create()
    {
        var task = new AnnotationTask();
        var session = new LabelingSession(Config, task);
        session.SetImageOriginalSize(1000, 500);
        return (session, task, session.AddPolygon([100, 900, 900, 100], [100, 100, 400, 400], "area"));
    }
    private static double[]?[] Curves() => [[250, 20, 750, 20], null, null, null];

    [Fact]
    public void CurveEdit_SavesReloadsAndUndoRedoRestoresControls()
    {
        var (session, task, row) = Create();
        Assert.True(session.UpdatePolygonPath(row.Id!, [100, 900, 900, 100], [100, 100, 400, 400], Curves()));
        var json = TaskJson.SerializeTask(task);
        var restored = new LabelingSession(Config, TaskJson.DeserializeTask(json)!);
        restored.SetImageOriginalSize(1000, 500);
        Assert.Equal(Curves()[0], restored.BuildRegionViews()[0].Curves[0]);
        session.Undo();
        Assert.All(session.BuildRegionViews()[0].Curves, Assert.Null);
        session.Redo();
        Assert.Equal(Curves()[0], session.BuildRegionViews()[0].Curves[0]);
    }

    [Fact]
    public void InvalidEdit_DoesNotChangeDataOrUndoHistory()
    {
        var (session, task, row) = Create();
        var before = TaskJson.SerializeTask(task);
        Assert.False(session.UpdatePolygonPath(row.Id!, [100, double.NaN, 900], [100, 100, 400], [null, null, null]));
        Assert.False(session.UpdatePolygonPath(row.Id!, [100, 900], [100, 100], [null, null]));
        Assert.False(session.UpdatePolygonPath(row.Id!, [100, 900, 900, 100], [100, 100, 400, 400], [[1, 2], null, null, null]));
        Assert.False(session.UpdatePolygonPath(row.Id!, [100, 900, 900, 100], [100, 100, 400, 400], [null, null, null, null]));
        Assert.Equal(before, TaskJson.SerializeTask(task));
        session.Undo();
        Assert.Empty(session.BuildRegionViews());
    }

    [Fact]
    public void MoveShape_TranslatesControlsAndLimitsWholePath()
    {
        var (session, _, row) = Create();
        session.UpdatePolygonPath(row.Id!, [100, 900, 900, 100], [100, 100, 400, 400], Curves());
        session.MoveShape(row.Id!, -1000, -1000);
        var view = session.BuildRegionViews()[0];
        Assert.Equal([150d, 0d, 650d, 0d], view.Curves[0]!);
        Assert.Equal(0, view.PointsX[0]); Assert.Equal(80, view.PointsY[0]);
        session.Undo();
        Assert.Equal(Curves()[0], session.BuildRegionViews()[0].Curves[0]);
    }

    [Fact]
    public void MoveVertex_MovesAttachedControlsOnly()
    {
        var (session, _, row) = Create();
        session.UpdatePolygonPath(row.Id!, [100, 900, 900, 100], [100, 100, 400, 400], Curves());
        session.MovePolygonVertex(row.Id!, 0, 120, 130);
        Assert.Equal([270d, 50d, 750d, 20d], session.BuildRegionViews()[0].Curves[0]!);
    }

    [Fact]
    public void Flatten_ApproximatesCurveWithinHalfPixelAndPreservesStraightVertices()
    {
        var (session, _, row) = Create();
        Assert.Equal(4, PolygonPath.Flatten(row).Count);
        session.UpdatePolygonPath(row.Id!, [100, 900, 900, 100], [100, 100, 400, 400], Curves());
        var points = PolygonPath.Flatten(row);
        Assert.InRange(points.Count, 5, 200);
        for (var i = 0; i <= 1000; i++)
        {
            var t = i / 1000d; var u = 1 - t;
            var x = u * u * u * 100 + 3 * u * u * t * 250 + 3 * u * t * t * 750 + t * t * t * 900;
            var y = u * u * u * 100 + 3 * u * u * t * 20 + 3 * u * t * t * 20 + t * t * t * 100;
            Assert.InRange(DistanceToPath(x, y, points), 0, 0.5);
        }
        Assert.Equal(new PolygonPath.Point(100, 400), points[^1]);
    }

    [Fact]
    public void SegmentExport_FlattensCurvesAndDoesNotMutateEditableAnnotation()
    {
        var (session, task, row) = Create();
        session.UpdatePolygonPath(row.Id!, [100, 900, 900, 100], [100, 100, 400, 400], Curves());
        var before = TaskJson.SerializeTask(task);
        var numbers = YoloLabelExporter.Build(task, YoloTaskType.Segment, ["area"]).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)
            .Skip(1).Select(n => double.Parse(n, CultureInfo.InvariantCulture)).ToArray();
        Assert.Equal(PolygonPath.Flatten(row).Count * 2, numbers.Length);
        Assert.All(numbers, n => Assert.InRange(n, 0, 1));
        Assert.Contains(numbers.Where((_, index) => index % 2 == 1), n => n < 0.2);
        Assert.Equal(before, TaskJson.SerializeTask(task));
    }

    [Fact]
    public void Flatten_HandlesCollinearCurveThatDoublesBack()
    {
        var row = new ResultRow { OriginalWidth = 100, OriginalHeight = 100, Value = new JsonObject
        {
            ["points"] = new JsonArray(new JsonArray(20, 20), new JsonArray(40, 20), new JsonArray(40, 80)),
            [PolygonPath.CurvesField] = new JsonArray(new JsonArray(100, 20, 0, 20), null, null)
        }};
        Assert.True(PolygonPath.Flatten(row).Count > 3);
    }

    [Fact]
    public void CurveEdit_RecordsDimensionsForImportedRowsAndCocoUsesSampledGeometry()
    {
        var (session, task, row) = Create();
        row.OriginalWidth = null; row.OriginalHeight = null;
        session.UpdatePolygonPath(row.Id!, [100, 900, 900, 100], [100, 100, 400, 400], Curves());
        Assert.Equal(1000, row.OriginalWidth); Assert.Equal(500, row.OriginalHeight);
        var exported = ExportService.Coco([task], LabelingConfigParser.Parse(Config));
        var root = JsonNode.Parse(exported.Files[0].Content)!;
        var annotation = root["annotations"]![0]!;
        var polygon = annotation["segmentation"]![0]!.AsArray();
        Assert.Equal(PolygonPath.Flatten(row).Count * 2, polygon.Count);
        Assert.True(annotation["area"]!.GetValue<double>() > 800 * 300);
        Assert.True(annotation["bbox"]![1]!.GetValue<int>() < 100);
        Assert.Equal(4, row.Value!["points"]!.AsArray().Count);
    }

    private static double DistanceToPath(double x, double y, IReadOnlyList<PolygonPath.Point> path)
    {
        var best = double.MaxValue;
        for (var i = 0; i < path.Count; i++)
        {
            var a = path[i]; var b = path[(i + 1) % path.Count]; var dx = b.X - a.X; var dy = b.Y - a.Y;
            var length = dx * dx + dy * dy;
            var t = length == 0 ? 0 : Math.Clamp(((x - a.X) * dx + (y - a.Y) * dy) / length, 0, 1);
            best = Math.Min(best, Math.Sqrt(Math.Pow(x - a.X - t * dx, 2) + Math.Pow(y - a.Y - t * dy, 2)));
        }
        return best;
    }
}
