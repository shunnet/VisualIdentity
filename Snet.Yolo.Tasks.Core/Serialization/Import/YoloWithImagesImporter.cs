namespace Snet.Yolo.Tasks.Core.Serialization.Import;

using System.Globalization;
using System.IO.Compression;
using Config = Snet.Yolo.Tasks.Core.Config;
using Editing = Snet.Yolo.Tasks.Core.Editing;

/// <summary>YOLO 归一化矩形标注。</summary>
/// <param name="ClassIndex">类别索引。</param>
/// <param name="XCenter">中心点 X，范围 0 到 1。</param>
/// <param name="YCenter">中心点 Y，范围 0 到 1。</param>
/// <param name="Width">宽度，范围 0 到 1。</param>
/// <param name="Height">高度，范围 0 到 1。</param>
public sealed record YoloImportBox(int ClassIndex, double XCenter, double YCenter, double Width, double Height);

/// <summary>一个已完成自检、等待落盘的 YOLO 图片条目。</summary>
/// <param name="ImageEntryPath">图片在 ZIP 中的规范相对路径。</param>
/// <param name="OriginalFileName">图片原始文件名。</param>
/// <param name="Boxes">与图片对应的标注框。</param>
public sealed record YoloImportImage(string ImageEntryPath, string OriginalFileName, IReadOnlyList<YoloImportBox> Boxes);

/// <summary>YOLO ZIP 自检结果。</summary>
/// <param name="Classes">按类别索引排序的标签名称。</param>
/// <param name="Images">通过目录和标注检查的图片。</param>
public sealed record YoloImportPlan(IReadOnlyList<string> Classes, IReadOnlyList<YoloImportImage> Images)
{
    /// <summary>包内标注框总数。</summary>
    public int AnnotationCount => Images.Sum(image => image.Boxes.Count);
}

/// <summary>把导入类别合并到项目标签配置后的结果。</summary>
/// <param name="Xml">合并且通过配置校验的 XML。</param>
/// <param name="ClassNames">每个 YOLO 类别索引最终对应的项目标签名称。</param>
public sealed record YoloLabelMergeResult(string Xml, IReadOnlyList<string> ClassNames);

/// <summary>读取并严格检查“YOLO (with images)”ZIP 数据集。</summary>
public static class YoloWithImagesImporter
{
    private static readonly HashSet<string> ImageExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".jpg", ".jpeg", ".png", ".gif", ".webp", ".bmp",
    };

    /// <summary>ZIP 中单张图片允许的最大编码大小：100 MiB。</summary>
    public const long MaximumImageBytes = 100L * 1024 * 1024;

    /// <summary>允许导入的最大图片数量，防止恶意 ZIP 消耗过多资源。</summary>
    public const int MaximumImageCount = 10_000;

    /// <summary>允许的 ZIP 解压后图片总大小上限：20 GiB。</summary>
    public const long MaximumTotalImageBytes = 20L * 1024 * 1024 * 1024;

    /// <summary>检查 ZIP 结构、类别、图片与标注，并返回不可变导入计划。</summary>
    /// <param name="archivePath">已上传到本机临时目录的 ZIP 文件。</param>
    /// <returns>通过全部自检的导入计划。</returns>
    public static YoloImportPlan Inspect(string archivePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(archivePath);
        using var stream = new FileStream(archivePath, FileMode.Open, FileAccess.Read, FileShare.Read, 128 * 1024, FileOptions.SequentialScan);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: false);
        return Inspect(archive);
    }

    /// <summary>检查已打开的 ZIP，调用方仍负责释放 ZIP。</summary>
    /// <param name="archive">只读 ZIP。</param>
    /// <returns>通过全部自检的导入计划。</returns>
    public static YoloImportPlan Inspect(ZipArchive archive)
    {
        ArgumentNullException.ThrowIfNull(archive);
        if (archive.Entries.Count > 30_000) { throw new InvalidDataException("压缩包文件数量超过 30,000 个限制。"); }
        var entries = IndexEntries(archive);
        var classEntry = entries.Values
            .Where(entry => entry.Path.EndsWith("/classes.txt", StringComparison.OrdinalIgnoreCase) || entry.Path.Equals("classes.txt", StringComparison.OrdinalIgnoreCase))
            .OrderBy(entry => entry.Path.Count(character => character == '/'))
            .ThenBy(entry => entry.Path.Length)
            .FirstOrDefault()
            ?? throw new InvalidDataException("压缩包缺少 classes.txt 标签文件。");
        var classes = ParseClasses(classEntry.Entry);

        var imageEntries = entries.Values
            .Where(entry => IsImagePath(entry.Path))
            .OrderBy(entry => entry.Path, StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (imageEntries.Count == 0) { throw new InvalidDataException("压缩包的 images 目录中没有图片。"); }
        if (imageEntries.Count > MaximumImageCount) { throw new InvalidDataException($"压缩包图片数量超过 {MaximumImageCount:N0} 张限制。"); }

        long totalImageBytes = 0;
        var importedImages = new List<YoloImportImage>(imageEntries.Count);
        var matchedLabels = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var imageEntry in imageEntries)
        {
            if (imageEntry.Entry.Length is <= 0 or > MaximumImageBytes)
            {
                throw new InvalidDataException($"图片 {imageEntry.Path} 为空或超过 100 MiB。");
            }
            totalImageBytes = checked(totalImageBytes + imageEntry.Entry.Length);
            if (totalImageBytes > MaximumTotalImageBytes) { throw new InvalidDataException("压缩包内图片总大小超过 20 GiB 限制。"); }

            var labelPath = CorrespondingLabelPath(imageEntry.Path);
            if (!entries.TryGetValue(labelPath, out var labelEntry))
            {
                throw new InvalidDataException($"图片 {imageEntry.Path} 缺少对应标注 {labelPath}。");
            }
            matchedLabels.Add(labelEntry.Path);
            var boxes = ParseBoxes(labelEntry.Entry, labelEntry.Path, classes.Count);
            importedImages.Add(new YoloImportImage(imageEntry.Path, Path.GetFileName(imageEntry.Path), boxes));
        }

        var unmatchedLabel = entries.Values.FirstOrDefault(entry => IsLabelPath(entry.Path) && !matchedLabels.Contains(entry.Path));
        if (unmatchedLabel is not null) { throw new InvalidDataException($"标注 {unmatchedLabel.Path} 没有对应图片。"); }
        return new YoloImportPlan(classes, importedImages);
    }

    /// <summary>把导入标签合并到矩形标签配置，并保证项目中所有标签颜色互不重复。</summary>
    /// <param name="labelConfigXml">当前项目标签 XML。</param>
    /// <param name="importedLabels">按 YOLO 类别索引排序的标签。</param>
    /// <returns>安全转义并通过配置校验的 XML，以及类别名称映射。</returns>
    public static YoloLabelMergeResult MergeLabels(string labelConfigXml, IReadOnlyList<string> importedLabels)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(labelConfigXml);
        ArgumentNullException.ThrowIfNull(importedLabels);
        var parsedConfig = Config.LabelingConfigParser.Parse(labelConfigXml);
        var rectangleControlName = parsedConfig.Controls.FirstOrDefault(control => control.Kind == Config.ControlTagKind.RectangleLabels)?.Name
            ?? throw new InvalidDataException("当前项目缺少矩形标签控件。");
        var document = System.Xml.Linq.XDocument.Parse(labelConfigXml, System.Xml.Linq.LoadOptions.PreserveWhitespace);
        var rectangle = document.Descendants().FirstOrDefault(element =>
            (element.Name.LocalName is "RectangleLabels" or "Rectangle") && string.Equals(element.Attribute("name")?.Value, rectangleControlName, StringComparison.Ordinal))
            ?? throw new InvalidDataException("当前项目缺少矩形标签控件。");

        var usedColors = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var option in document.Descendants().Where(element => element.Name.LocalName is "Label" or "Choice"))
        {
            var color = NormalizeHexColor(option.Attribute("background")?.Value);
            if (!usedColors.Add(color))
            {
                color = Editing.LabelPalette.NextDistinctColor(usedColors);
                usedColors.Add(color);
            }
            option.SetAttributeValue("background", color);
        }

        var existingNames = rectangle.Elements().Where(element => element.Name.LocalName == "Label")
            .Select(element => element.Attribute("value")?.Value ?? string.Empty)
            .Where(value => value.Length > 0)
            .ToDictionary(value => value, value => value, StringComparer.OrdinalIgnoreCase);
        var resolvedClassNames = new List<string>(importedLabels.Count);
        foreach (var label in importedLabels)
        {
            if (existingNames.TryGetValue(label, out var existingName)) { resolvedClassNames.Add(existingName); continue; }
            var color = Editing.LabelPalette.NextDistinctColor(usedColors);
            usedColors.Add(color);
            rectangle.Add(new System.Xml.Linq.XElement("Label", new System.Xml.Linq.XAttribute("value", label), new System.Xml.Linq.XAttribute("background", color)));
            existingNames.Add(label, label);
            resolvedClassNames.Add(label);
        }

        var xml = document.ToString();
        var validation = Config.ConfigValidator.Validate(Config.LabelingConfigParser.Parse(xml));
        if (!validation.IsValid) { throw new InvalidDataException("导入标签生成的项目配置无效。"); }
        return new YoloLabelMergeResult(xml, resolvedClassNames);
    }

    /// <summary>建立安全且不区分大小写的 ZIP 条目索引。</summary>
    private static Dictionary<string, IndexedEntry> IndexEntries(ZipArchive archive)
    {
        var entries = new Dictionary<string, IndexedEntry>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in archive.Entries)
        {
            if (string.IsNullOrEmpty(entry.Name)) { continue; }
            var path = NormalizeEntryPath(entry.FullName);
            if (!entries.TryAdd(path, new IndexedEntry(path, entry)))
            {
                throw new InvalidDataException($"压缩包包含重复路径：{path}。");
            }
        }
        return entries;
    }

    /// <summary>规范化 ZIP 相对路径并阻止目录穿越。</summary>
    private static string NormalizeEntryPath(string rawPath)
    {
        var path = rawPath.Replace('\\', '/');
        if (path.Length == 0 || path[0] == '/' || Path.IsPathRooted(path))
        {
            throw new InvalidDataException($"压缩包包含非法绝对路径：{rawPath}。");
        }
        var segments = path.Split('/');
        if (segments.Length == 0 || segments.Any(segment => segment.Length == 0 || segment is "." or ".." || segment.IndexOf('\0') >= 0))
        {
            throw new InvalidDataException($"压缩包包含非法路径：{rawPath}。");
        }
        return string.Join('/', segments);
    }

    /// <summary>判断条目是否是 images 目录中的受支持图片。</summary>
    private static bool IsImagePath(string path) =>
        ContainsDirectory(path, "images") && ImageExtensions.Contains(Path.GetExtension(path));

    /// <summary>判断条目是否是 labels 目录中的标注文本。</summary>
    private static bool IsLabelPath(string path) =>
        ContainsDirectory(path, "labels") && path.EndsWith(".txt", StringComparison.OrdinalIgnoreCase);

    /// <summary>判断规范路径是否包含指定目录段。</summary>
    private static bool ContainsDirectory(string path, string directory) =>
        path.Split('/').Any(segment => segment.Equals(directory, StringComparison.OrdinalIgnoreCase));

    /// <summary>根据图片路径生成同级 labels 目录下的标注路径。</summary>
    private static string CorrespondingLabelPath(string imagePath)
    {
        var segments = imagePath.Split('/');
        var imageDirectoryIndex = Array.FindLastIndex(segments, segment => segment.Equals("images", StringComparison.OrdinalIgnoreCase));
        if (imageDirectoryIndex < 0) { throw new InvalidDataException($"图片路径不在 images 目录中：{imagePath}。"); }
        segments[imageDirectoryIndex] = "labels";
        segments[^1] = Path.GetFileNameWithoutExtension(segments[^1]) + ".txt";
        return string.Join('/', segments);
    }

    /// <summary>解析 classes.txt，兼容“每行名称”和“索引 名称”两种格式。</summary>
    private static IReadOnlyList<string> ParseClasses(ZipArchiveEntry entry)
    {
        var lines = ReadText(entry, 1024 * 1024).Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (lines.Length == 0) { throw new InvalidDataException("classes.txt 没有标签。"); }
        var indexed = new List<(int Index, string Name)>();
        var allIndexed = true;
        foreach (var line in lines)
        {
            var separator = line.IndexOfAny([' ', '\t']);
            if (separator <= 0 || !int.TryParse(line[..separator], NumberStyles.None, CultureInfo.InvariantCulture, out var index))
            {
                allIndexed = false;
                break;
            }
            var name = line[(separator + 1)..].Trim();
            if (name.Length == 0) { throw new InvalidDataException($"classes.txt 的类别 {index} 名称为空。"); }
            indexed.Add((index, name));
        }
        var classes = allIndexed
            ? indexed.OrderBy(item => item.Index).Select((item, expected) => item.Index == expected ? item.Name : throw new InvalidDataException("classes.txt 的类别索引必须从 0 连续递增。")).ToArray()
            : lines.Select(line => line.Trim()).ToArray();
        if (classes.Any(string.IsNullOrWhiteSpace) || classes.Distinct(StringComparer.OrdinalIgnoreCase).Count() != classes.Length)
        {
            throw new InvalidDataException("classes.txt 包含空名称或重复标签。");
        }
        return classes;
    }

    /// <summary>解析并验证一个 YOLO 标注文本。</summary>
    private static IReadOnlyList<YoloImportBox> ParseBoxes(ZipArchiveEntry entry, string path, int classCount)
    {
        var text = ReadText(entry, 8 * 1024 * 1024);
        var boxes = new List<YoloImportBox>();
        var lineNumber = 0;
        foreach (var rawLine in text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            lineNumber++;
            var fields = rawLine.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (fields.Length != 5) { throw new InvalidDataException($"{path} 第 {lineNumber} 行不是 5 列 YOLO 矩形标注。"); }
            if (!int.TryParse(fields[0], NumberStyles.None, CultureInfo.InvariantCulture, out var classIndex) || classIndex < 0 || classIndex >= classCount)
            {
                throw new InvalidDataException($"{path} 第 {lineNumber} 行的类别索引超出 classes.txt 范围。");
            }
            var values = new double[4];
            for (var index = 0; index < values.Length; index++)
            {
                if (!double.TryParse(fields[index + 1], NumberStyles.Float, CultureInfo.InvariantCulture, out values[index]) || !double.IsFinite(values[index]))
                {
                    throw new InvalidDataException($"{path} 第 {lineNumber} 行包含无效坐标。");
                }
            }
            if (values[0] is < 0 or > 1 || values[1] is < 0 or > 1 || values[2] is <= 0 or > 1 || values[3] is <= 0 or > 1)
            {
                throw new InvalidDataException($"{path} 第 {lineNumber} 行坐标必须位于 0 到 1，宽高必须大于 0。");
            }
            const double tolerance = 0.000001;
            if (values[0] - values[2] / 2 < -tolerance || values[0] + values[2] / 2 > 1 + tolerance || values[1] - values[3] / 2 < -tolerance || values[1] + values[3] / 2 > 1 + tolerance)
            {
                throw new InvalidDataException($"{path} 第 {lineNumber} 行的标注框超出图片范围。");
            }
            boxes.Add(new YoloImportBox(classIndex, values[0], values[1], values[2], values[3]));
        }
        return boxes;
    }

    /// <summary>在指定字节上限内，以严格 UTF-8 读取文本条目。</summary>
    private static string ReadText(ZipArchiveEntry entry, long maximumBytes)
    {
        if (entry.Length < 0 || entry.Length > maximumBytes) { throw new InvalidDataException($"文本条目 {entry.FullName} 过大。"); }
        using var stream = entry.Open();
        using var reader = new StreamReader(stream, new System.Text.UTF8Encoding(false, true), detectEncodingFromByteOrderMarks: true);
        try { return reader.ReadToEnd(); }
        catch (System.Text.DecoderFallbackException exception) { throw new InvalidDataException($"文本条目 {entry.FullName} 不是有效 UTF-8。", exception); }
    }

    /// <summary>把已有颜色规范为六位十六进制格式；无效颜色使用色板首色。</summary>
    private static string NormalizeHexColor(string? color)
    {
        if (string.IsNullOrWhiteSpace(color)) { return Editing.LabelPalette.ColorForIndex(0); }
        var value = color.Trim();
        if (value.Length == 4 && value[0] == '#' && value[1..].All(Uri.IsHexDigit))
        {
            return $"#{value[1]}{value[1]}{value[2]}{value[2]}{value[3]}{value[3]}".ToUpperInvariant();
        }
        if (value.Length == 7 && value[0] == '#' && value[1..].All(Uri.IsHexDigit)) { return value.ToUpperInvariant(); }
        return Editing.LabelPalette.ColorForIndex(0);
    }

    /// <summary>带规范路径的 ZIP 条目。</summary>
    private sealed record IndexedEntry(string Path, ZipArchiveEntry Entry);
}
