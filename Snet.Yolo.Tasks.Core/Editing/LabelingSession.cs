namespace Snet.Yolo.Tasks.Core.Editing;

using Snet.Yolo.Tasks.Core.Config;
using Snet.Yolo.Tasks.Core.Geometry;
using Snet.Yolo.Tasks.Core.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;

/// <summary>
/// 标注会话：图像标注编辑器核心状态（单一真源）。
/// 支持矩形/多边形/关键点/椭圆四类几何区域；
/// 几何以像素空间操作，序列化为 0–100% 百分数（与 LS 一致）；
/// 每次变更前入历史快照支持撤销/重做；非几何结果行原样透传。
/// </summary>
public sealed class LabelingSession
{
    private readonly List<string> _undoStack = new();
    private readonly List<string> _redoStack = new();
    private readonly Dictionary<ControlTagKind, ControlTagInfo> _shapeControls = new();
    private static readonly IReadOnlyDictionary<ControlTagKind, (string RegionType, string LabelField)> ShapeMap =
        new Dictionary<ControlTagKind, (string, string)>
        {
            [ControlTagKind.RectangleLabels] = (RegionType.RectangleLabels, "rectanglelabels"),
            [ControlTagKind.PolygonLabels] = (RegionType.PolygonLabels, "polygonlabels"),
            [ControlTagKind.KeyPointLabels] = (RegionType.KeyPointLabels, "keypointlabels"),
            [ControlTagKind.EllipseLabels] = (RegionType.EllipseLabels, "ellipselabels"),
            [ControlTagKind.BrushLabels] = (RegionType.BrushLabels, "brushlabels"),
        };

    /// <summary>当前会话对应的任务。</summary>
    public AnnotationTask Task { get; }

    /// <summary>标签配置模型。</summary>
    public LabelingConfigModel Config { get; }

    /// <summary>配置校验结果。</summary>
    public ConfigValidationResult Validation { get; }

    /// <summary>当前标注（变更直接作用于其 Result）。</summary>
    public Annotation CurrentAnnotation { get; }

    /// <summary>图像原始尺寸（像素）；由 SetImageOriginalSize 设置。</summary>
    public double? OriginalWidth { get; private set; }

    /// <summary>图像原始尺寸（像素）。</summary>
    public double? OriginalHeight { get; private set; }

    /// <summary>选中的区域行 id（界面态，不参与持久化）。</summary>
    public string? SelectedRegionId { get; set; }

    /// <summary>可撤销。</summary>
    public bool CanUndo => _undoStack.Count > 0;

    /// <summary>可重做。</summary>
    public bool CanRedo => _redoStack.Count > 0;

    /// <summary>全部几何行（矩形/多边形/关键点/椭圆，按类型过滤）。</summary>
    public IEnumerable<ResultRow> GeometryRows
        => CurrentAnnotation.Result.Where(row => ShapeMap.Values.Any(mapping => mapping.RegionType == row.Type));

    /// <summary>矩形行（兼容旧接口）。</summary>
    public IEnumerable<ResultRow> RectRows => CurrentAnnotation.Result.Where(row => row.Type == RegionType.RectangleLabels);

    /// <summary>构造会话。</summary>
    public LabelingSession(string labelConfigXml, AnnotationTask task)
    {
        Task = task ?? throw new ArgumentNullException(nameof(task));
        Config = LabelingConfigParser.Parse(labelConfigXml);
        Validation = ConfigValidator.Validate(Config);

        foreach (var control in Config.Controls.Where(control => ShapeMap.ContainsKey(control.Kind)))
        {
            _shapeControls.TryAdd(control.Kind, control);
        }

        CurrentAnnotation = task.Annotations.FirstOrDefault(annotation => annotation.WasCancelled != true)
            ?? new Annotation { Id = JsonValue.Create(Guid.NewGuid().ToString("N")[..8]) };
    }

    /// <summary>设置图像原始尺寸（像素）。</summary>
    public void SetImageOriginalSize(double width, double height)
    {
        OriginalWidth = width;
        OriginalHeight = height;
    }

    /// <summary>是否具备某形状的绘制前提（含控件且配置有效且图像就绪）。</summary>
    public bool CanDraw(ControlTagKind kind) => _shapeControls.ContainsKey(kind) && Validation.IsValid && OriginalWidth.HasValue;

    /// <summary>兼容旧接口：矩形是否可绘制。</summary>
    public bool CanDrawRectangle => CanDraw(ControlTagKind.RectangleLabels);

    /// <summary>某形状的控制标签（无则空）。</summary>
    public IReadOnlyList<LabelOptionInfo> GetControlLabels(ControlTagKind kind)
        => _shapeControls.TryGetValue(kind, out var control) ? control.Labels : Array.Empty<LabelOptionInfo>();

    /// <summary>兼容旧接口：矩形控制标签。</summary>
    public IReadOnlyList<LabelOptionInfo> RectangleLabels => GetControlLabels(ControlTagKind.RectangleLabels);

    /// <summary>兼容旧接口：矩形控制标签信息。</summary>
    public ControlTagInfo? RectangleControl => _shapeControls.GetValueOrDefault(ControlTagKind.RectangleLabels);

    // ── 新增：多边形/关键点/椭圆 ──

    /// <summary>新增矩形区域（像素空间）。</summary>
    public ResultRow AddRectangle(PixelRect rect, string? label)
    {
        EnsureGeometryReady();
        var control = ControlFor(ControlTagKind.RectangleLabels);
        var row = NewRow(control, RegionType.RectangleLabels);
        ApplyPixelRectToRow(row, ClampPixelRect(rect));
        ValueAccess.SetStringList(row.Value!, "rectanglelabels", label is null ? Array.Empty<string>() : new[] { label });
        return Commit(row);
    }
    /// <summary>新增多边形区域。</summary>
    public ResultRow AddPolygon(IReadOnlyList<double> pointsX, IReadOnlyList<double> pointsY, string? label)
    {
        EnsureGeometryReady();
        ArgumentNullException.ThrowIfNull(pointsX);
        ArgumentNullException.ThrowIfNull(pointsY);
        if (pointsX.Count != pointsY.Count || pointsX.Count < 3)
        {
            throw new ArgumentException("多边形必须包含至少三个数量一致的 X/Y 坐标。");
        }

        var control = ControlFor(ControlTagKind.PolygonLabels);
        var row = NewRow(control, RegionType.PolygonLabels);
        var value = row.Value!;
        var points = new JsonArray();
        for (var index = 0; index < pointsX.Count; index++)
        {
            points.Add(new JsonArray(
                PercentMath.PixelsToPercent(ClampFinite(pointsX[index], 0d, OriginalWidth!.Value), OriginalWidth.Value),
                PercentMath.PixelsToPercent(ClampFinite(pointsY[index], 0d, OriginalHeight!.Value), OriginalHeight.Value)));
        }

        value["points"] = points;
        ValueAccess.SetStringList(value, "polygonlabels", label is null ? Array.Empty<string>() : new[] { label });
        return Commit(row);
    }

    /// <summary>新增关键点区域。</summary>
    public ResultRow AddKeyPoint(double x, double y, string? label)
    {
        EnsureGeometryReady();
        var control = ControlFor(ControlTagKind.KeyPointLabels);
        var row = NewRow(control, RegionType.KeyPointLabels);
        var value = row.Value!;
        ValueAccess.SetDouble(value, "x", PercentMath.PixelsToPercent(ClampFinite(x, 0d, OriginalWidth!.Value), OriginalWidth.Value));
        ValueAccess.SetDouble(value, "y", PercentMath.PixelsToPercent(ClampFinite(y, 0d, OriginalHeight!.Value), OriginalHeight.Value));
        ValueAccess.SetDouble(value, "width", 1d);
        ValueAccess.SetStringList(value, "keypointlabels", label is null ? Array.Empty<string>() : new[] { label });
        return Commit(row);
    }

    /// <summary>新增椭圆区域（中心 + 半径，像素）。</summary>
    public ResultRow AddEllipse(double centerX, double centerY, double radiusX, double radiusY, string? label)
    {
        EnsureGeometryReady();
        var control = ControlFor(ControlTagKind.EllipseLabels);
        var row = NewRow(control, RegionType.EllipseLabels);
        var value = row.Value!;
        var rect = ClampPixelRect(new PixelRect(centerX - radiusX, centerY - radiusY, radiusX * 2d, radiusY * 2d));
        ValueAccess.SetDouble(value, "x", PercentMath.PixelsToPercent(rect.X + rect.Width / 2d, OriginalWidth!.Value));
        ValueAccess.SetDouble(value, "y", PercentMath.PixelsToPercent(rect.Y + rect.Height / 2d, OriginalHeight!.Value));
        ValueAccess.SetDouble(value, "radiusX", PercentMath.PixelsToPercent(rect.Width / 2d, OriginalWidth!.Value));
        ValueAccess.SetDouble(value, "radiusY", PercentMath.PixelsToPercent(rect.Height / 2d, OriginalHeight!.Value));
        ValueAccess.SetDouble(value, "rotation", 0d);
        ValueAccess.SetStringList(value, "ellipselabels", label is null ? Array.Empty<string>() : new[] { label });
        return Commit(row);
    }

    /// <summary>按类型调整区域的像素边界。</summary>
    public void ResizeShape(string regionId, double x, double y, double width, double height)
    {
        var row = FindRow(regionId);
        if (row is null || row.Value is null) { return; }
        if (row.Type is not (RegionType.RectangleLabels or RegionType.EllipseLabels)) { return; }
        Checkpoint();
        EnsureGeometryReady();
        switch (row.Type)
        {
            case RegionType.RectangleLabels:
                ApplyPixelRectToRow(row, ClampPixelRect(new PixelRect(x, y, width, height)));
                break;
            case RegionType.EllipseLabels:
                var rect = ClampPixelRect(new PixelRect(x, y, width, height));
                ValueAccess.SetDouble(row.Value, "x", PercentMath.PixelsToPercent(rect.X + rect.Width / 2d, OriginalWidth!.Value));
                ValueAccess.SetDouble(row.Value, "y", PercentMath.PixelsToPercent(rect.Y + rect.Height / 2d, OriginalHeight!.Value));
                ValueAccess.SetDouble(row.Value, "radiusX", PercentMath.PixelsToPercent(rect.Width / 2d, OriginalWidth.Value));
                ValueAccess.SetDouble(row.Value, "radiusY", PercentMath.PixelsToPercent(rect.Height / 2d, OriginalHeight.Value));
                break;
        }
    }

    /// <summary>将多边形指定顶点移到图像范围内的像素坐标。</summary>
    public void MovePolygonVertex(string regionId, int index, double x, double y)
    {
        var row = FindRow(regionId);
        if (row is null || row.Value is null || row.Value["points"] is not JsonArray pts || OriginalWidth is null || OriginalHeight is null) { return; }
        Checkpoint();
        if (index >= 0 && index < pts.Count && pts[index] is JsonArray pt && pt.Count >= 2)
        {
            pt[0] = PercentMath.PixelsToPercent(ClampFinite(x, 0d, OriginalWidth.Value), OriginalWidth.Value);
            pt[1] = PercentMath.PixelsToPercent(ClampFinite(y, 0d, OriginalHeight.Value), OriginalHeight.Value);
        }
    }

    /// <summary>按像素增量平移区域，整体限制在原始图像内并保持形状。</summary>
    public void MoveShape(string regionId, double deltaX, double deltaY)
    {
        var row = FindRow(regionId);
        if (row is null || row.Value is null)
        {
            return;
        }

        Checkpoint();
        EnsureGeometryReady();
        var value = row.Value;
        switch (row.Type)
        {
            case RegionType.RectangleLabels:
                var rect = GetPixelRect(row);
                deltaX = LimitTranslation(deltaX, rect.X, rect.X + rect.Width, OriginalWidth!.Value);
                deltaY = LimitTranslation(deltaY, rect.Y, rect.Y + rect.Height, OriginalHeight!.Value);
                ApplyPixelRectToRow(row, TranslateRect(rect, deltaX, deltaY));
                break;
            case RegionType.BrushLabels:
                var brushX = ReadPointArray(row.Value, "pointxs");
                var brushY = ReadPointArray(row.Value, "pointys");
                if (brushX.Length == 0 || brushY.Length != brushX.Length) { break; }
                deltaX = LimitTranslation(deltaX, brushX.Min(), brushX.Max(), OriginalWidth!.Value);
                deltaY = LimitTranslation(deltaY, brushY.Min(), brushY.Max(), OriginalHeight!.Value);
                SetBrushGeometry(row, brushX.Select(point => point + deltaX).ToArray(), brushY.Select(point => point + deltaY).ToArray(), ValueAccess.GetDouble(value, "size"));
                break;
            case RegionType.PolygonLabels:
                var (px, py) = GetPolygonPx(row);
                if (px.Length == 0 || py.Length != px.Length) { break; }
                deltaX = LimitTranslation(deltaX, px.Min(), px.Max(), OriginalWidth!.Value);
                deltaY = LimitTranslation(deltaY, py.Min(), py.Max(), OriginalHeight!.Value);
                var points = new JsonArray();
                for (var index = 0; index < px.Length; index++)
                {
                    points.Add(new JsonArray(
                        PercentMath.PixelsToPercent(px[index] + deltaX, OriginalWidth!.Value),
                        PercentMath.PixelsToPercent(py[index] + deltaY, OriginalHeight!.Value)));
                }

                value["points"] = points;
                break;
            case RegionType.KeyPointLabels:
                ValueAccess.SetDouble(value, "x", PercentMath.PixelsToPercent(Clamp(GetKeyPointX(row) + deltaX, 0, OriginalWidth!.Value), OriginalWidth!.Value));
                ValueAccess.SetDouble(value, "y", PercentMath.PixelsToPercent(Clamp(GetKeyPointY(row) + deltaY, 0, OriginalHeight!.Value), OriginalHeight!.Value));
                break;
            case RegionType.EllipseLabels:
                var (ex, ey, rx, ry) = GetEllipsePx(row);
                deltaX = LimitTranslation(deltaX, ex - rx, ex + rx, OriginalWidth!.Value);
                deltaY = LimitTranslation(deltaY, ey - ry, ey + ry, OriginalHeight!.Value);
                ValueAccess.SetDouble(value, "x", PercentMath.PixelsToPercent(ex + deltaX, OriginalWidth.Value));
                ValueAccess.SetDouble(value, "y", PercentMath.PixelsToPercent(ey + deltaY, OriginalHeight.Value));
                ValueAccess.SetDouble(value, "radiusX", PercentMath.PixelsToPercent(rx, OriginalWidth!.Value));
                ValueAccess.SetDouble(value, "radiusY", PercentMath.PixelsToPercent(ry, OriginalHeight!.Value));
                break;
        }
    }

    /// <summary>设置子区域（如关键点）的父区域 id（层级，用于 COCO/YOLO 关键点导出）。</summary>
    public void SetParentRegion(string childId, string? parentId)
    {
        var row = FindRow(childId);
        if (row is null) { return; }
        if (!string.IsNullOrEmpty(parentId) && !CurrentAnnotation.Result.Any(r => r.Id == parentId)) { return; }
        Checkpoint();
        row.ParentId = string.IsNullOrEmpty(parentId) ? null : parentId;
    }
    /// <summary>更新矩形几何。</summary>
    public void UpdateRectangleGeometry(string regionId, PixelRect rect)
    {
        var row = FindRow(regionId);
        if (row is null)
        {
            return;
        }

        Checkpoint();
        ApplyPixelRectToRow(row, ClampPixelRect(rect));
    }

    /// <summary>使用 Label Studio 的 0–100 百分比单位更新矩形详情，并保持合法边界。</summary>
    public void UpdateRectanglePercentGeometry(string regionId, double x, double y, double width, double height, double rotation)
    {
        var row = FindRow(regionId);
        if (row?.Value is null || row.Type != RegionType.RectangleLabels) { return; }
        Checkpoint();
        x = ClampFinite(x, 0d, 99.99d);
        y = ClampFinite(y, 0d, 99.99d);
        width = ClampFinite(width, 0.01d, 100d - x);
        height = ClampFinite(height, 0.01d, 100d - y);
        ValueAccess.SetDouble(row.Value, "x", x);
        ValueAccess.SetDouble(row.Value, "y", y);
        ValueAccess.SetDouble(row.Value, "width", width);
        ValueAccess.SetDouble(row.Value, "height", height);
        ValueAccess.SetDouble(row.Value, "rotation", double.IsFinite(rotation) ? rotation : 0d);
    }

    /// <summary>覆盖式重设标签（按区域类型选择字段）。</summary>
    public void SetRegionLabels(string regionId, IReadOnlyCollection<string> labels)
    {
        var row = FindRow(regionId);
        if (row is null || row.Value is null)
        {
            return;
        }

        Checkpoint();
        var field = ShapeMap.Values.FirstOrDefault(mapping => mapping.RegionType == row.Type).LabelField;
        ValueAccess.SetStringList(row.Value, field, labels);
    }

    /// <summary>删除区域行（含选中清理）。</summary>
    public void RemoveRegion(string regionId)
    {
        var row = FindRow(regionId);
        if (row is null)
        {
            return;
        }

        Checkpoint();
        CurrentAnnotation.Result.Remove(row);
        if (SelectedRegionId == regionId)
        {
            SelectedRegionId = null;
        }
    }

    /// <summary>矩形像素几何读取。</summary>
    public PixelRect GetPixelRect(ResultRow row)
    {
        EnsureGeometryReady();
        var value = row.Value ?? throw new InvalidOperationException("区域缺少 value。");
        return new PixelRect(
            PercentMath.PercentToPixels(ValueAccess.GetDouble(value, "x"), OriginalWidth!.Value),
            PercentMath.PercentToPixels(ValueAccess.GetDouble(value, "y"), OriginalHeight!.Value),
            PercentMath.PercentToPixels(ValueAccess.GetDouble(value, "width"), OriginalWidth!.Value),
            PercentMath.PercentToPixels(ValueAccess.GetDouble(value, "height"), OriginalHeight!.Value));
    }

    /// <summary>多边形像素点读取。</summary>
    public (double[] X, double[] Y) GetPolygonPx(ResultRow row)
    {
        EnsureGeometryReady();
        var value = row.Value ?? throw new InvalidOperationException("区域缺少 value。");
        var pointArray = value["points"]?.AsArray() ?? new JsonArray();
        var xs = new double[pointArray.Count];
        var ys = new double[pointArray.Count];
        for (var index = 0; index < pointArray.Count; index++)
        {
            var pair = pointArray[index]!.AsArray();
            xs[index] = PercentMath.PercentToPixels(JsonNumber(pair[0]), OriginalWidth!.Value);
            ys[index] = PercentMath.PercentToPixels(JsonNumber(pair[1]), OriginalHeight!.Value);
        }

        return (xs, ys);
    }
    private double GetKeyPointX(ResultRow row) => PercentMath.PercentToPixels(ValueAccess.GetDouble(row.Value!, "x"), OriginalWidth!.Value);
    private double GetKeyPointY(ResultRow row) => PercentMath.PercentToPixels(ValueAccess.GetDouble(row.Value!, "y"), OriginalHeight!.Value);

    private (double X, double Y, double Rx, double Ry) GetEllipsePx(ResultRow row)
    {
        EnsureGeometryReady();
        var value = row.Value ?? throw new InvalidOperationException("区域缺少 value。");
        return (
            PercentMath.PercentToPixels(ValueAccess.GetDouble(value, "x"), OriginalWidth!.Value),
            PercentMath.PercentToPixels(ValueAccess.GetDouble(value, "y"), OriginalHeight!.Value),
            PercentMath.PercentToPixels(ValueAccess.GetDouble(value, "radiusX"), OriginalWidth!.Value),
            PercentMath.PercentToPixels(ValueAccess.GetDouble(value, "radiusY"), OriginalHeight!.Value));
    }

    /// <summary>构建画布同步视图（矩形/多边形/关键点/椭圆）。</summary>
    public List<RegionView> BuildRegionViews()
    {
        var views = new List<RegionView>();
        var index = 0;
        foreach (var row in GeometryRows)
        {
            if (OriginalWidth is null || OriginalHeight is null || row.Value is null)
            {
                continue;
            }

            var labels = RowLabels(row);
            var view = new RegionView
            {
                Id = row.Id ?? string.Empty,
                Type = row.Type,
                LabelText = string.Join(", ", labels),
                Color = RegionColor(row, labels, index),
                Selected = row.Id == SelectedRegionId,
            };

            switch (row.Type)
            {
                case RegionType.RectangleLabels:
                    var rect = GetPixelRect(row);
                    view.X = rect.X; view.Y = rect.Y; view.Width = rect.Width; view.Height = rect.Height;
                    view.Rotation = ValueAccess.GetDouble(row.Value, "rotation");
                    break;
                case RegionType.PolygonLabels:
                    var (px, py) = GetPolygonPx(row);
                    view.PointsX = px; view.PointsY = py;
                    break;
                case RegionType.BrushLabels:
                    view.PointsX = ReadPointArray(row.Value, "pointxs");
                    view.PointsY = ReadPointArray(row.Value, "pointys");
                    view.BrushSize = ValueAccess.GetDouble(row.Value, "size");
                    break;

                case RegionType.KeyPointLabels:
                    view.Kx = GetKeyPointX(row); view.Ky = GetKeyPointY(row);
                    break;
                case RegionType.EllipseLabels:
                    var (ex, ey, rx, ry) = GetEllipsePx(row);
                    view.Ex = ex; view.Ey = ey; view.Rx = rx; view.Ry = ry;
                    view.Rotation = ValueAccess.GetDouble(row.Value, "rotation");
                    break;
            }

            views.Add(view);
            index++;
        }

        return views;
    }

    private List<string> RowLabels(ResultRow row)
    {
        var field = ShapeMap.Values.FirstOrDefault(mapping => mapping.RegionType == row.Type).LabelField;
        return ValueAccess.GetStringList(row.Value!, field);
    }

    private string RegionColor(ResultRow row, IReadOnlyList<string> labels, int index)
    {
        var kind = ShapeMap.FirstOrDefault(pair => pair.Value.RegionType == row.Type).Key;
        var options = GetControlLabels(kind);
        return LabelPalette.ResolveColor(options, labels.FirstOrDefault(), index);
    }

    /// <summary>撤销。</summary>
    public void Undo()
    {
        if (_undoStack.Count == 0)
        {
            return;
        }

        _redoStack.Add(Snapshot());
        Restore(_undoStack[^1]);
        _undoStack.RemoveAt(_undoStack.Count - 1);
        SelectedRegionId = null;
    }

    /// <summary>重做。</summary>
    public void Redo()
    {
        if (_redoStack.Count == 0)
        {
            return;
        }

        _undoStack.Add(Snapshot());
        Restore(_redoStack[^1]);
        _redoStack.RemoveAt(_redoStack.Count - 1);
        SelectedRegionId = null;
    }

    private void Checkpoint()
    {
        _undoStack.Add(Snapshot());
        if (_undoStack.Count > 200)
        {
            _undoStack.RemoveAt(0);
        }

        _redoStack.Clear();
    }

    private string Snapshot() => Serialization.TaskJson.SerializeTask(WrapCurrent());

    private void Restore(string snapshot)
    {
        var wrapper = System.Text.Json.JsonSerializer.Deserialize<AnnotationTask>(snapshot);
        var annotation = wrapper?.Annotations.FirstOrDefault();
        if (annotation is not null)
        {
            CurrentAnnotation.Result.Clear();
            CurrentAnnotation.Result.AddRange(annotation.Result);
        }
    }

    private AnnotationTask WrapCurrent()
    {
        var wrapper = new AnnotationTask { Id = Task.Id };
        var clone = new Annotation { Id = CurrentAnnotation.Id?.DeepClone() };
        clone.Result.AddRange(CurrentAnnotation.Result.Select(CloneRow));
        wrapper.Annotations.Add(clone);
        return wrapper;
    }

    private ResultRow Commit(ResultRow row)
    {
        Checkpoint();
        CurrentAnnotation.Result.Add(row);
        SelectedRegionId = row.Id;
        return row;
    }

    private ResultRow NewRow(ControlTagInfo control, string regionType)
    {
        if (control is null)
        {
            throw new InvalidOperationException("缺少 " + regionType + " 对应的控制标签；请检查 Labeling Config。");
        }

        return new ResultRow
        {
            Id = Guid.NewGuid().ToString("N")[..8],
            Type = regionType,
            FromName = control.Name,
            ToName = control.ToName,
            Origin = "manual",
            Source = "$" + ImageObjectName(),
            OriginalWidth = OriginalWidth,
            OriginalHeight = OriginalHeight,
            ImageRotation = 0,
            Value = new JsonObject(),
        };
    }

    private ControlTagInfo ControlFor(ControlTagKind kind)
    {
        if (!_shapeControls.TryGetValue(kind, out var control))
        {
            throw new InvalidOperationException("标签配置缺少对应控件。");
        }

        return control;
    }

    private ResultRow? FindRow(string regionId) => CurrentAnnotation.Result.FirstOrDefault(row => row.Id == regionId);

    private string ImageObjectName()
    {
        var imageObject = Config.Objects.FirstOrDefault(obj => obj.Kind == ObjectTagKind.Image);
        return imageObject?.Name ?? string.Empty;
    }

    private static ResultRow CloneRow(ResultRow row)
    {
        var json = System.Text.Json.JsonSerializer.Serialize(row);
        return System.Text.Json.JsonSerializer.Deserialize<ResultRow>(json)!;
    }

    private void ApplyPixelRectToRow(ResultRow row, PixelRect rect)
    {
        var value = row.Value!;
        value["x"] = PercentMath.PixelsToPercent(rect.X, OriginalWidth!.Value);
        value["y"] = PercentMath.PixelsToPercent(rect.Y, OriginalHeight!.Value);
        value["width"] = PercentMath.PixelsToPercent(rect.Width, OriginalWidth!.Value);
        value["height"] = PercentMath.PixelsToPercent(rect.Height, OriginalHeight!.Value);
        if (value["rotation"] is null) { value["rotation"] = 0d; }
    }

    /// <summary>将矩形限制在原始图像像素范围内。</summary>
    private PixelRect ClampPixelRect(PixelRect rect)
    {
        EnsureGeometryReady();
        var imageWidth = OriginalWidth!.Value;
        var imageHeight = OriginalHeight!.Value;
        var x = ClampFinite(rect.X, 0d, Math.Max(0d, imageWidth - 0.5d));
        var y = ClampFinite(rect.Y, 0d, Math.Max(0d, imageHeight - 0.5d));
        var width = ClampFinite(rect.Width, 0.5d, imageWidth - x);
        var height = ClampFinite(rect.Height, 0.5d, imageHeight - y);
        return new PixelRect(x, y, width, height);
    }

    /// <summary>限制有限数值；非有限值回退到下界。</summary>
    private static double ClampFinite(double value, double min, double max)
        => double.IsFinite(value) ? Math.Clamp(value, min, Math.Max(min, max)) : min;

    /// <summary>限制平移增量，使区域的最小值和最大值仍在图像边界内。</summary>
    private static double LimitTranslation(double requested, double currentMin, double currentMax, double boundary)
        => ClampFinite(requested, -currentMin, boundary - currentMax);

    private static PixelRect TranslateRect(PixelRect rect, double deltaX, double deltaY)
        => new PixelRect(rect.X + deltaX, rect.Y + deltaY, rect.Width, rect.Height);

    private static double Clamp(double value, double min, double max) => Math.Max(min, Math.Min(max, value));

    /// <summary>新增笔刷掩码区域：沿点列栅格化掩码并 RLE 编码，同时保留点列/笔宽用于描边渲染。</summary>
    public ResultRow AddBrushMask(IReadOnlyList<double> pointsX, IReadOnlyList<double> pointsY, double size, string? label)
    {
        EnsureGeometryReady();
        ArgumentNullException.ThrowIfNull(pointsX);
        ArgumentNullException.ThrowIfNull(pointsY);
        if (pointsX.Count != pointsY.Count || pointsX.Count < 2)
        {
            throw new ArgumentException("笔刷轨迹必须包含至少两个数量一致的 X/Y 坐标。");
        }

        var control = ControlFor(ControlTagKind.BrushLabels);
        var row = NewRow(control, RegionType.BrushLabels);
        SetBrushGeometry(row, pointsX, pointsY, size);
        ValueAccess.SetStringList(row.Value!, "brushlabels", label is null ? Array.Empty<string>() : new[] { label });
        return Commit(row);
    }

    /// <summary>写入笔刷点列并重建 RLE 掩码，保证画布几何与导出数据一致。</summary>
    private void SetBrushGeometry(ResultRow row, IReadOnlyList<double> pointsX, IReadOnlyList<double> pointsY, double size)
    {
        var width = (int)OriginalWidth!.Value;
        var height = (int)OriginalHeight!.Value;
        var mask = new byte[checked(width * height)];
        size = ClampFinite(size, 1d, Math.Max(width, height));
        var radius = size / 2.0;
        var safeX = pointsX.Select(point => ClampFinite(point, 0d, width)).ToArray();
        var safeY = pointsY.Select(point => ClampFinite(point, 0d, height)).ToArray();
        for (var index = 0; index < pointsX.Count; index++)
        {
            PaintCircle(mask, width, height, safeX[index], safeY[index], radius);
            if (index > 0) { PaintSegment(mask, width, height, safeX[index - 1], safeY[index - 1], safeX[index], safeY[index], radius); }
        }
        var value = row.Value!;
        value["format"] = "rle";
        value["rle"] = JsonArrayOf(RleCodec.Encode(mask).Select(item => (int)item));
        value["size"] = size;
        value["pointxs"] = JsonArrayOf(safeX);
        value["pointys"] = JsonArrayOf(safeY);
    }

    private static JsonArray JsonArrayOf<T>(IEnumerable<T> items)
    {
        var array = new JsonArray();
        foreach (var item in items) { array.Add(item); }
        return array;
    }

    private static void PaintCircle(byte[] mask, int width, int height, double cx, double cy, double radius)
    {
        var x0 = Math.Max(0, (int)(cx - radius)); var x1 = Math.Min(width - 1, (int)(cx + radius));
        var y0 = Math.Max(0, (int)(cy - radius)); var y1 = Math.Min(height - 1, (int)(cy + radius));
        for (var y = y0; y <= y1; y++)
        {
            for (var x = x0; x <= x1; x++)
            {
                if ((x - cx) * (x - cx) + (y - cy) * (y - cy) <= radius * radius) { mask[y * width + x] = 255; }
            }
        }
    }

    private static void PaintSegment(byte[] mask, int width, int height, double x0, double y0, double x1, double y1, double radius)
    {
        var steps = Math.Max(1, (int)Math.Max(Math.Abs(x1 - x0), Math.Abs(y1 - y0)));
        for (var step = 0; step <= steps; step++)
        {
            var t = step / (double)steps;
            PaintCircle(mask, width, height, x0 + (x1 - x0) * t, y0 + (y1 - y0) * t, radius);
        }
    }

    private static double[] ReadPointArray(JsonObject value, string name)
        => value[name]?.AsArray()?.Select(item => JsonNumber(item)).ToArray() ?? Array.Empty<double>();

    private void EnsureGeometryReady()
    {
        if (!OriginalWidth.HasValue || !OriginalHeight.HasValue)
        {
            throw new InvalidOperationException("图像原始尺寸未就绪，无法进行像素几何运算。");
        }
    }
    private static double JsonNumber(JsonNode? node)
    {
        if (node is not JsonValue value)
        {
            return 0d;
        }

        if (value.TryGetValue<int>(out var intValue)) { return intValue; }
        if (value.TryGetValue<double>(out var doubleValue)) { return doubleValue; }
        if (value.TryGetValue<long>(out var longValue)) { return longValue; }
        if (value.TryGetValue<decimal>(out var decimalValue)) { return (double)decimalValue; }
        return 0d;
    }
}
