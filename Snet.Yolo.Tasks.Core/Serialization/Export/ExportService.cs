
namespace Snet.Yolo.Tasks.Core.Serialization.Export;

using Snet.Yolo.Tasks.Core.Config;
using Snet.Yolo.Tasks.Core.Editing;
using Snet.Yolo.Tasks.Core.Geometry;
using Snet.Yolo.Tasks.Core.Models;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Xml.Linq;
/// <summary>
/// 导出服务：与 Label Studio 支持格式一致（JSON/JSON_MIN/CSV/TSV/COCO/PascalVOC/YOLO/CoNLL2003/ASR_MANIFEST）。
/// 坐标按官方单位换算：region value 为 0–100 百分数 → 像素 = percent/100 × original。
/// </summary>
public static class ExportService
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = false, PropertyNameCaseInsensitive = false };

    private static string ImageFieldName(LabelingConfigModel config)
    {
        var image = config.Objects.FirstOrDefault(obj => obj.Kind == ObjectTagKind.Image);
        return image?.ValueField?.TrimStart('$') ?? "image";
    }

    private static string DataString(JsonObject? data, string field) => data?[field]?.ToString() ?? string.Empty;

    private static double Px(double percent, bool isHorizontal, ResultRow row)
        => PercentMath.PercentToPixels(percent, isHorizontal ? (row.OriginalWidth ?? 100) : (row.OriginalHeight ?? 100));

    /// <summary>Exports complete annotation tasks as JSON.</summary>
    public static ExportResult Json(IEnumerable<AnnotationTask> tasks, LabelingConfigModel config)
        => Single("tasks.json", TaskJson.SerializeTasks(tasks));

    /// <summary>Exports compact task data and results as JSON.</summary>
    public static ExportResult JsonMin(IEnumerable<AnnotationTask> tasks, LabelingConfigModel config)
    {
        var roots = new JsonArray();
        foreach (var task in tasks)
        {
            var root = task.Data?.DeepClone() as JsonObject ?? new JsonObject();
            var annotation = task.Annotations.FirstOrDefault(a => a.WasCancelled != true);
            if (annotation is not null)
            {
                foreach (var row in annotation.Result)
                {
                    if (row.Value is null || string.IsNullOrEmpty(row.FromName)) { continue; }
                    var value = row.Value.DeepClone();
                    if (row.ParentId is not null) { value["parentID"] = row.ParentId; }
                    if (root[row.FromName] is JsonArray existing) { existing.Add(value); }
                    else { root[row.FromName] = new JsonArray(value); }
                }
            }
            roots.Add(root);
        }
        return Single("tasks-min.json", roots.ToJsonString(JsonOptions));
    }

    /// <summary>Exports task data as CSV.</summary>
    public static ExportResult Csv(IEnumerable<AnnotationTask> tasks, LabelingConfigModel config) => Tabular(tasks, config, ",");
    /// <summary>Exports task data as TSV.</summary>
    public static ExportResult Tsv(IEnumerable<AnnotationTask> tasks, LabelingConfigModel config) => Tabular(tasks, config, "\t");

    private static ExportResult Tabular(IEnumerable<AnnotationTask> tasks, LabelingConfigModel config, string separator)
    {
        var columns = config.Controls.Select(control => control.Name).Where(name => !string.IsNullOrEmpty(name)).Distinct().ToList();
        var builder = new StringBuilder();
        builder.AppendLine("# from_name" + separator + string.Join(separator, columns));
        foreach (var task in tasks)
        {
            var cells = columns.Select(column => "[]").ToList();
            var annotation = task.Annotations.FirstOrDefault(a => a.WasCancelled != true);
            if (annotation is not null)
            {
                foreach (var row in annotation.Result)
                {
                    if (row.Value is null || string.IsNullOrEmpty(row.FromName)) { continue; }
                    var index = columns.IndexOf(row.FromName);
                    if (index < 0) { continue; }
                    cells[index] = row.Value.ToJsonString(JsonOptions);
                }
            }
            builder.AppendLine(cells.Select(cell => Quote(cell, separator)).Aggregate((a, b) => a + separator + b));
        }
        var ext = separator == "\t" ? "tsv" : "csv";
        return Single("export." + ext, builder.ToString());
    }

    private static string Quote(string value, string separator)
    {
        if (separator == "," && (value.Contains(",") || value.Contains("\"")))
        {
            return "\"" + value.Replace("\"", "\"\"") + "\"";
        }
        return value;
    }

    /// <summary>Exports image annotations in COCO format.</summary>
    public static ExportResult Coco(IEnumerable<AnnotationTask> tasks, LabelingConfigModel config)
    {
        var imageField = ImageFieldName(config);
        var categories = CollectCocoCategories(config);
        var keyPointNames = CollectKeyPointNames(config);
        var root = new JsonObject();
        root["images"] = new JsonArray();
        root["categories"] = ToCocoCategoriesJson(categories, keyPointNames);
        root["annotations"] = new JsonArray();
        var imageId = 0;
        var annotationId = 0;
        foreach (var task in tasks)
        {
            imageId++;
            var image = new JsonObject { ["id"] = imageId, ["file_name"] = ResolveFileName(DataString(task.Data, imageField)) };
            ((JsonArray)root["images"]!).Add(image);
            var annotation = task.Annotations.FirstOrDefault(a => a.WasCancelled != true);
            if (annotation is null) { continue; }
            foreach (var row in annotation.Result)
            {
                if (row.Value is null) { continue; }
                var categoryId = CategoryId(categories, row);
                if (categoryId <= 0) { continue; }
                var item = new JsonObject { ["id"] = ++annotationId, ["image_id"] = imageId, ["category_id"] = categoryId, ["iscrowd"] = 0 };
                if (row.Type == RegionType.RectangleLabels)
                {
                    if (!string.IsNullOrEmpty(row.Id) && annotation.Result.Any(candidate => candidate.Type == RegionType.KeyPointLabels && candidate.ParentId == row.Id))
                    {
                        continue;
                    }
                    item["bbox"] = ToBboxArray(row);
                    item["area"] = AreaOf(row);
                    item["segmentation"] = new JsonArray();
                }
                else if (row.Type == RegionType.PolygonLabels)
                {
                    var points = ToSegmentation(row, out var enclosedBbox);
                    item["segmentation"] = points;
                    item["bbox"] = enclosedBbox;
                    item["area"] = Math.Abs(PolygonArea(row));
                }
                else { continue; }
                item["ignore"] = 0;
                ((JsonArray)root["annotations"]!).Add(item);
            }
            EmitCocoKeypoints(root, annotation, categories, keyPointNames, imageId, ref annotationId);
        }
        return Single("coco.json", root.ToJsonString(JsonOptions));
    }

    /// <summary>Exports image annotations as Pascal VOC XML files.</summary>
    public static ExportResult Voc(IEnumerable<AnnotationTask> tasks, LabelingConfigModel config)
    {
        var imageField = ImageFieldName(config);
        var files = new List<ExportFile>();
        var id = 0;
        foreach (var task in tasks)
        {
            id++;
            var annotation = task.Annotations.FirstOrDefault(a => a.WasCancelled != true);
            if (annotation is null) { continue; }
            var objects = new List<XElement>();
            foreach (var row in annotation.Result.Where(row => row.Type == RegionType.RectangleLabels && row.Value is not null))
            {
                var labels = ValueLabels(row.Value!, row.Type);
                var name = labels.FirstOrDefault() ?? "object";
                objects.Add(new XElement("object",
                    new XElement("name", name),
                    new XElement("bndbox",
                        new XElement("xmin", (int)Math.Round(Px(Num(row.Value!, "x"), true, row))),
                        new XElement("ymin", (int)Math.Round(Px(Num(row.Value!, "y"), false, row))),
                        new XElement("xmax", (int)Math.Round(Px(Num(row.Value!, "x") + Num(row.Value!, "width"), true, row))),
                        new XElement("ymax", (int)Math.Round(Px(Num(row.Value!, "y") + Num(row.Value!, "height"), false, row))))));
            }
            var document = new XDocument(new XElement("annotation",
                new XElement("folder", "images"),
                new XElement("filename", ResolveFileName(DataString(task.Data, imageField))),
                new XElement("size", new XElement("width", 0), new XElement("height", 0), new XElement("depth", 3)),
                objects));
            files.Add(new ExportFile("Annotations/" + id + ".xml", Encoding.UTF8.GetBytes(document.ToString(SaveOptions.DisableFormatting))));
        }
        return Multi("voc.zip", files);
    }

    /// <summary>Exports YOLO labels and dataset metadata.</summary>
    public static ExportResult Yolo(IEnumerable<AnnotationTask> tasks, LabelingConfigModel config)
    {
        var categories = CollectCategories(config);
        var lines = categories.Select((c, index) => index + " " + c).ToList();
        var files = new List<ExportFile> { new ExportFile("classes.txt", Encoding.UTF8.GetBytes(string.Join("\n", lines) + "\n")) };
        var id = 0;
        foreach (var task in tasks)
        {
            id++;
            var annotation = task.Annotations.FirstOrDefault(a => a.WasCancelled != true);
            if (annotation is null) { continue; }
            var builder = new StringBuilder();
            foreach (var row in annotation.Result.Where(row => row.Type == RegionType.RectangleLabels && row.Value is not null))
            {
                var index = categories.IndexOf(ValueLabels(row.Value!, row.Type).FirstOrDefault() ?? string.Empty);
                if (index < 0) { continue; }
                var xc = Num(row.Value!, "x") + Num(row.Value!, "width") / 2d;
                var yc = Num(row.Value!, "y") + Num(row.Value!, "height") / 2d;
                builder.Append(CultureInfo.InvariantCulture, $"{index} {F(xc / 100d)} {F(yc / 100d)} {F(Num(row.Value!, "width") / 100d)} {F(Num(row.Value!, "height") / 100d)}\n");
            }
            if (builder.Length > 0)
            {
                files.Add(new ExportFile("labels/" + id + ".txt", Encoding.UTF8.GetBytes(builder.ToString())));
            }
        }
        return Multi("yolo.zip", files);
    }

    /// <summary>Exports YOLO labels together with source images.</summary>
    public static ExportResult YoloWithImages(IEnumerable<AnnotationTask> tasks, LabelingConfigModel config, Func<string, byte[]?> imageLoader)
    {
        ArgumentNullException.ThrowIfNull(imageLoader);
        var taskList = tasks.ToList();
        var files = Yolo(taskList, config).Files.ToList();
        for (var index = 0; index < taskList.Count; index++)
        {
            var imageRef = DataString(taskList[index].Data, "image");
            if (string.IsNullOrWhiteSpace(imageRef)) { continue; }
            var content = imageLoader(imageRef);
            if (content is null) { continue; }
            var labelPath = $"labels/{index + 1}.txt";
            if (files.All(file => !file.Path.Equals(labelPath, StringComparison.OrdinalIgnoreCase)))
            {
                // YOLO 以空标注文件表示“图片中没有目标”，同时保证导出的 ZIP 可以严格自检后重新导入。
                files.Add(new ExportFile(labelPath, Array.Empty<byte>()));
            }
            var extension = Path.GetExtension(ResolveFileName(imageRef));
            files.Add(new ExportFile($"images/{index + 1}{extension}", content));
        }
        return Multi("yolo-with-images.zip", files);
    }

    /// <summary>Exports named-entity annotations in CoNLL 2003 format.</summary>
    public static ExportResult Conll(IEnumerable<AnnotationTask> tasks, LabelingConfigModel config)
    {
        var builder = new StringBuilder("\n");
        foreach (var task in tasks)
        {
            var text = DataString(task.Data, "text");
            if (string.IsNullOrWhiteSpace(text)) { continue; }
            var spans = new List<(int Start, int End, string Type)>();
            var annotation = task.Annotations.FirstOrDefault(a => a.WasCancelled != true);
            if (annotation is not null)
            {
                foreach (var row in annotation.Result.Where(row => row.Type == "labels" && row.Value is not null))
                {
                    var labels = ValueLabels(row.Value!, row.Type);
                    spans.Add(((int)Num(row.Value!, "start"), (int)Num(row.Value!, "end"), labels.FirstOrDefault() ?? "O"));
                }
            }
            foreach (var token in Tokenize(text))
            {
                builder.Append(token.Text).Append('\t').Append("O").Append('\t').Append('\t').Append(FindTag(token.Start, token.End, spans)).Append('\n');
            }
            builder.Append('\n');
        }
        return Single("export.conll", builder.ToString());
    }

    /// <summary>Exports audio transcription tasks as an ASR manifest.</summary>
    public static ExportResult AsrManifest(IEnumerable<AnnotationTask> tasks, LabelingConfigModel config)
    {
        var builder = new StringBuilder();
        foreach (var task in tasks)
        {
            var audio = DataString(task.Data, "audio");
            var annotation = task.Annotations.FirstOrDefault(a => a.WasCancelled != true);
            if (annotation is null || string.IsNullOrEmpty(audio)) { continue; }
            var texts = annotation.Result.Where(row => row.Type == RegionType.TextArea && row.Value is not null)
                .SelectMany(row => row.Value!["text"]?.AsArray()?.Select(item => item?.GetValue<string>() ?? string.Empty) ?? Array.Empty<string>())
                .ToList();
            var manifest = new JsonObject { ["audio_filepath"] = audio, ["text"] = texts.FirstOrDefault() ?? string.Empty };
            if (annotation.Result.FirstOrDefault(row => row.Type == RegionType.Labels)?.Value is { } segment)
            {
                manifest["offset"] = Num(segment, "start");
                manifest["duration"] = Num(segment, "end") - Num(segment, "start");
            }
            builder.AppendLine(manifest.ToJsonString(JsonOptions));
        }
        return Single("asr_manifest.jsonl", builder.ToString());
    }

    /// <summary>Brush→PNG 掩码导出：按标签合并笔刷掩码，输出每标签一张 PNG。</summary>
    public static ExportResult BrushPng(IEnumerable<AnnotationTask> tasks, LabelingConfigModel config)
    {
        var perLabel = new Dictionary<string, byte[]>();
        var dims = new Dictionary<string, (int W, int H)>();
        foreach (var task in tasks)
        {
            var annotation = task.Annotations.FirstOrDefault(a => a.WasCancelled != true);
            if (annotation is null) { continue; }
            foreach (var row in annotation.Result.Where(row => row.Type == RegionType.BrushLabels && row.Value is not null))
            {
                var label = ValueLabels(row.Value!, row.Type).FirstOrDefault() ?? "mask";
                var width = (int)(row.OriginalWidth ?? 100);
                var height = (int)(row.OriginalHeight ?? 100);
                var rle = row.Value!["rle"]?.AsArray()?.Select(item => (byte)(item?.GetValue<int>() ?? 0)).ToArray() ?? Array.Empty<byte>();
                var mask = Snet.Yolo.Tasks.Core.Editing.RleCodec.Decode(rle, width * height);
                if (perLabel.TryGetValue(label, out var existing))
                {
                    for (var i = 0; i < mask.Length; i++) { existing[i] = (byte)Math.Max(existing[i], mask[i]); }
                }
                else
                {
                    perLabel[label] = mask;
                    dims[label] = (width, height);
                }
            }
        }

        var files = new List<ExportFile>();
        foreach (var pair in perLabel)
        {
            var (w, h) = dims[pair.Key];
            var png = Snet.Yolo.Tasks.Core.Editing.PngEncoder.EncodeMask(pair.Value, w, h);
            files.Add(new ExportFile("masks/" + SanitizeName(pair.Key) + ".png", png));
        }
        return Multi("brush_masks.zip", files);
    }

    private static string SanitizeName(string name)
    {
        foreach (var c in Path.GetInvalidFileNameChars()) { name = name.Replace(c, '_'); }
        return name;
    }

    private static ExportResult Single(string fileName, string text)
        => new() { FileName = fileName, IsZip = false, Files = new[] { new ExportFile(fileName, Encoding.UTF8.GetBytes(text)) } };

    private static ExportResult Multi(string zipName, List<ExportFile> files)
        => new() { FileName = zipName, IsZip = true, Files = files };

    private static List<string> CollectCategories(LabelingConfigModel config)
    {
        var categories = new List<string>();
        foreach (var control in config.Controls)
        {
            foreach (var label in control.Labels)
            {
                if (!categories.Contains(label.Value)) { categories.Add(label.Value); }
            }
        }
        return categories;
    }

    private static JsonArray ToCategoriesJson(List<string> categories)
    {
        var array = new JsonArray();
        for (var index = 0; index < categories.Count; index++)
        {
            array.Add(new JsonObject { ["id"] = index + 1, ["name"] = categories[index], ["supercategory"] = "object" });
        }
        return array;
    }

    /// <summary>收集 COCO 对象类别，关键点名称不应被误当作对象类别。</summary>
    private static List<string> CollectCocoCategories(LabelingConfigModel config)
        => config.Controls
            .Where(control => control.Kind is ControlTagKind.RectangleLabels or ControlTagKind.PolygonLabels or ControlTagKind.BrushLabels)
            .SelectMany(control => control.Labels)
            .Select(label => label.Value)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.Ordinal)
            .ToList();

    /// <summary>按 model_index 和配置顺序返回 COCO 固定关键点序列。</summary>
    private static List<string> CollectKeyPointNames(LabelingConfigModel config)
        => config.Controls
            .Where(control => control.Kind == ControlTagKind.KeyPointLabels)
            .SelectMany(control => control.Labels.Select((label, index) => new { label.Value, Order = label.ModelIndex ?? index }))
            .OrderBy(item => item.Order)
            .Select(item => item.Value)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.Ordinal)
            .ToList();

    /// <summary>生成带固定关键点元数据的 COCO 类别定义。</summary>
    private static JsonArray ToCocoCategoriesJson(List<string> categories, IReadOnlyList<string> keyPointNames)
    {
        var array = new JsonArray();
        for (var index = 0; index < categories.Count; index++)
        {
            var category = new JsonObject { ["id"] = index + 1, ["name"] = categories[index], ["supercategory"] = "object" };
            if (keyPointNames.Count > 0)
            {
                var keyPoints = new JsonArray();
                foreach (var keyPointName in keyPointNames) { keyPoints.Add(keyPointName); }
                category["keypoints"] = keyPoints;
                category["skeleton"] = new JsonArray();
            }
            array.Add(category);
        }
        return array;
    }

    private static int CategoryId(List<string> categories, ResultRow row)
        => categories.IndexOf(ValueLabels(row.Value!, row.Type).FirstOrDefault() ?? string.Empty) + 1;

    private static void EmitCocoKeypoints(JsonObject root, Annotation annotation, List<string> categories, IReadOnlyList<string> configuredKeyPoints, int imageId, ref int annotationId)
    {
        var rects = annotation.Result
            .Where(row => row.Type == RegionType.RectangleLabels && !string.IsNullOrEmpty(row.Id))
            .ToDictionary(row => row.Id!, StringComparer.Ordinal);
        var keyPointGroups = annotation.Result
            .Where(row => row.Type == RegionType.KeyPointLabels && row.Value is not null && !string.IsNullOrEmpty(row.ParentId))
            .GroupBy(row => row.ParentId!, StringComparer.Ordinal);
        foreach (var group in keyPointGroups)
        {
            if (!rects.TryGetValue(group.Key, out var rectangle)) { continue; }
            var categoryId = CategoryId(categories, rectangle);
            if (categoryId <= 0) { continue; }
            var rowsByName = group
                .Select(row => (Name: ValueLabels(row.Value!, row.Type).FirstOrDefault(), Row: row))
                .Where(item => !string.IsNullOrWhiteSpace(item.Name))
                .GroupBy(item => item.Name!, StringComparer.Ordinal)
                .ToDictionary(items => items.Key, items => items.First().Row, StringComparer.Ordinal);
            var keyPointNames = configuredKeyPoints.Count > 0 ? configuredKeyPoints : rowsByName.Keys.ToArray();
            var keypoints = new JsonArray();
            var visibleCount = 0;
            foreach (var keyPointName in keyPointNames)
            {
                if (!rowsByName.TryGetValue(keyPointName, out var row))
                {
                    keypoints.Add(0); keypoints.Add(0); keypoints.Add(0);
                    continue;
                }
                keypoints.Add((int)Math.Round(Px(ValueAccess.GetDouble(row.Value!, "x"), true, row)));
                keypoints.Add((int)Math.Round(Px(ValueAccess.GetDouble(row.Value!, "y"), false, row)));
                keypoints.Add(2);
                visibleCount++;
            }
            var item = new JsonObject
            {
                ["id"] = ++annotationId,
                ["image_id"] = imageId,
                ["category_id"] = categoryId,
                ["keypoints"] = keypoints,
                ["num_keypoints"] = visibleCount,
                ["bbox"] = ToBboxArray(rectangle),
                ["area"] = AreaOf(rectangle),
                ["iscrowd"] = 0,
                ["ignore"] = 0,
            };
            ((JsonArray)root["annotations"]!).Add(item);
        }
    }
    private static JsonArray ToBboxArray(ResultRow row)
    {
        var value = row.Value!;
        return new JsonArray(
            (int)Math.Round(Px(Num(value, "x"), true, row)),
            (int)Math.Round(Px(Num(value, "y"), false, row)),
            (int)Math.Round(Px(Num(value, "width"), true, row)),
            (int)Math.Round(Px(Num(value, "height"), false, row)));
    }

    private static double AreaOf(ResultRow row)
    {
        var value = row.Value!;
        return Px(Num(value, "width"), true, row) * Px(Num(value, "height"), false, row);
    }

    private static JsonArray ToSegmentation(ResultRow row, out JsonArray bbox)
    {
        var value = row.Value!;
        var points = value["points"]?.AsArray() ?? new JsonArray();
        var segmentation = new JsonArray();
        var minX = double.MaxValue; var minY = double.MaxValue; var maxX = 0d; var maxY = 0d;
        foreach (var point in points)
        {
            var pair = point!.AsArray();
            var x = Px(JsonNum(pair[0]), true, row);
            var y = Px(JsonNum(pair[1]), false, row);
            segmentation.Add(Math.Round(x, 2));
            segmentation.Add(Math.Round(y, 2));
            minX = Math.Min(minX, x); minY = Math.Min(minY, y); maxX = Math.Max(maxX, x); maxY = Math.Max(maxY, y);
        }
        bbox = new JsonArray((int)Math.Round(minX), (int)Math.Round(minY), (int)Math.Round(maxX - minX), (int)Math.Round(maxY - minY));
        return segmentation;
    }

    private static double PolygonArea(ResultRow row)
    {
        var value = row.Value!;
        var points = value["points"]?.AsArray() ?? new JsonArray();
        double area = 0;
        for (var i = 0; i < points.Count; i++)
        {
            var a = points[i]!.AsArray();
            var b = points[(i + 1) % points.Count]!.AsArray();
            var ax = Px(JsonNum(a[0]), true, row);
            var ay = Px(JsonNum(a[1]), false, row);
            var bx = Px(JsonNum(b[0]), true, row);
            var by = Px(JsonNum(b[1]), false, row);
            area += ax * by - bx * ay;
        }
        return area / 2d;
    }

    private static double Num(JsonObject value, string name) => ValueAccess.GetDouble(value, name);

    private static double JsonNum(JsonNode? node)
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

    private static string F(double value) => value.ToString("0.######", CultureInfo.InvariantCulture);

    private static List<string> ValueLabels(JsonObject value, string type)
    {
        var field = type switch
        {
            RegionType.RectangleLabels => "rectanglelabels",
            RegionType.PolygonLabels => "polygonlabels",
            RegionType.KeyPointLabels => "keypointlabels",
            RegionType.EllipseLabels => "ellipselabels",
            RegionType.Labels => "labels",
            _ => "labels",
        };
        return ValueAccess.GetStringList(value, field);
    }

    /// <summary>从 URL/路径提取文件名（避免目录穿越）。</summary>
    public static string ResolveFileName(string urlOrPath)
    {
        if (string.IsNullOrEmpty(urlOrPath)) { return "image.jpg"; }
        var name = urlOrPath.Split('/').Last().Split('?')[0].Split('#')[0];
        return string.IsNullOrEmpty(name) ? "image.jpg" : name;
    }

    private static List<(string Text, int Start, int End)> Tokenize(string text)
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

    private static string FindTag(int tokenStart, int tokenEnd, List<(int Start, int End, string Type)> spans)
    {
        foreach (var span in spans.OrderBy(s => s.Start))
        {
            if (tokenStart >= span.Start && tokenEnd <= span.End)
            {
                var isBegin = tokenStart == span.Start || !spans.Any(s => s.Start < tokenStart && s.End >= tokenEnd && s.Type == span.Type);
                return (isBegin ? "B-" : "I-") + span.Type;
            }
        }
        return "O";
    }
}
