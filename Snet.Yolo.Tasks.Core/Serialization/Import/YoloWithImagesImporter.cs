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
/// <param name="UnlabeledImageCount">没有任何标注框的图片数量（含缺少标注文件的图片）。</param>
public sealed record YoloImportPlan(IReadOnlyList<string> Classes, IReadOnlyList<YoloImportImage> Images, int UnlabeledImageCount = 0)
{
    /// <summary>包内标注框总数。</summary>
    public int AnnotationCount => Images.Sum(image => image.Boxes.Count);
}

/// <summary>把导入类别合并到项目标签配置后的结果。</summary>
/// <param name="Xml">合并且通过配置校验的 XML。</param>
/// <param name="ClassNames">每个 YOLO 类别索引最终对应的项目标签名称。</param>
public sealed record YoloLabelMergeResult(string Xml, IReadOnlyList<string> ClassNames);

/// <summary>
/// 读取并严格检查带图片的 YOLO 数据集 ZIP。
///
/// 同时接受两种来源：
/// 1) 本应用导出的“YOLO (with images)”包（根目录 classes.txt + images/labels）；
/// 2) Roboflow / Ultralytics 导出的数据集包（data.yaml 描述类别，train|valid|test 下各自带 images/labels）。
/// </summary>
public static class YoloWithImagesImporter
{
    private static readonly HashSet<string> ImageExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".jpg", ".jpeg", ".png", ".gif", ".webp", ".bmp",
    };

    /// <summary>ZIP 中单张图片允许的最大编码大小：100 MiB。</summary>
    public const long MaximumImageBytes = 100L * 1024 * 1024;

    /// <summary>允许导入的最大图片数量，防止恶意 ZIP 消耗过多资源。</summary>
    public const int MaximumImageCount = 100_000;

    /// <summary>允许的 ZIP 条目数量上限（图片 + 标注 + 描述文件，留出余量）。</summary>
    public const int MaximumEntryCount = MaximumImageCount * 2 + 4_096;

    /// <summary>允许的 ZIP 解压后图片总大小上限：64 GiB。</summary>
    public const long MaximumTotalImageBytes = 64L * 1024 * 1024 * 1024;

    /// <summary>单个标注文本条目允许读取的最大字节数。</summary>
    private const long MaximumLabelBytes = 8L * 1024 * 1024;

    /// <summary>classes.txt / data.yaml 等描述文件允许读取的最大字节数。</summary>
    private const long MaximumDescriptorBytes = 4L * 1024 * 1024;

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
        if (archive.Entries.Count > MaximumEntryCount) { throw new InvalidDataException($"压缩包文件数量超过 {MaximumEntryCount:N0} 个限制。"); }
        var entries = IndexEntries(archive);
        var classes = ReadClassNames(entries);

        var imageEntries = entries.Values
            .Where(entry => IsImagePath(entry.Path))
            .OrderBy(entry => entry.Path, StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (imageEntries.Count == 0) { throw new InvalidDataException("压缩包的 images 目录中没有受支持的图片。"); }
        if (imageEntries.Count > MaximumImageCount) { throw new InvalidDataException($"压缩包图片数量超过 {MaximumImageCount:N0} 张限制。"); }

        long totalImageBytes = 0;
        var importedImages = new List<YoloImportImage>(imageEntries.Count);
        var matchedLabels = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var unlabeledImages = 0;
        foreach (var imageEntry in imageEntries)
        {
            if (imageEntry.Entry.Length is <= 0 or > MaximumImageBytes)
            {
                throw new InvalidDataException($"图片 {imageEntry.Path} 为空或超过 100 MiB。");
            }
            totalImageBytes = checked(totalImageBytes + imageEntry.Entry.Length);
            if (totalImageBytes > MaximumTotalImageBytes) { throw new InvalidDataException("压缩包内图片总大小超过 64 GiB 限制。"); }

            // 标注文件缺失不是错误：YOLO 生态里“没有 .txt”就等价于“这张图没有任何目标”，
            // Roboflow 导出的数据集也常常不给无标注图片生成空标注文件。
            // 反过来（有标注却没有图片）才是真正的包损坏，仍然报错。
            var labelPath = CorrespondingLabelPath(imageEntry.Path);
            IReadOnlyList<YoloImportBox> boxes = Array.Empty<YoloImportBox>();
            if (entries.TryGetValue(labelPath, out var labelEntry))
            {
                matchedLabels.Add(labelEntry.Path);
                boxes = ParseBoxes(labelEntry.Entry, labelEntry.Path, classes.Count);
            }
            else
            {
                unlabeledImages++;
            }
            importedImages.Add(new YoloImportImage(imageEntry.Path, Path.GetFileName(imageEntry.Path), boxes));
        }

        ThrowIfOrphanLabelExists(entries, matchedLabels);
        ThrowIfAnnotationsAreOutsideLabelsDirectory(entries, importedImages, unlabeledImages);
        return new YoloImportPlan(classes, importedImages, unlabeledImages);
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

        // 工程里可能存在"仅大小写不同"的重名标签（手工编辑标签时容易留下）：
        // 这里按"先出现者优先"建表，绝不能直接用 ToDictionary —— 那会抛 ArgumentException，
        // 结果是这个工程之后每次上传 ZIP 都失败，而且报错完全看不出原因。
        var existingNames = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var element in rectangle.Elements().Where(element => element.Name.LocalName == "Label"))
        {
            var value = element.Attribute("value")?.Value;
            if (!string.IsNullOrEmpty(value)) { existingNames.TryAdd(value, value); }
        }
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

    /// <summary>
    /// 读取类别名称：优先 classes.txt，其次 Roboflow / Ultralytics 的 data.yaml。
    /// 两者都可能被放在子目录里（Roboflow 会在 train、valid、test 下各放一份内容相同的 data.yaml）。
    /// </summary>
    private static IReadOnlyList<string> ReadClassNames(Dictionary<string, IndexedEntry> entries)
    {
        if (FindDescriptor(entries, "classes.txt") is { } classesEntry) { return ParseClasses(classesEntry.Entry); }
        var dataEntry = FindDescriptor(entries, "data.yaml") ?? FindDescriptor(entries, "data.yml");
        if (dataEntry is not null) { return ParseDataYamlClasses(ReadText(dataEntry.Entry, MaximumDescriptorBytes), dataEntry.Path); }
        throw new InvalidDataException("压缩包缺少 classes.txt 或 data.yaml 标签文件。");
    }

    /// <summary>按“路径层级最浅、路径最短”挑选描述文件，保证结果稳定可复现。</summary>
    private static IndexedEntry? FindDescriptor(Dictionary<string, IndexedEntry> entries, string fileName)
        => entries.Values
            .Where(entry => entry.Path.Equals(fileName, StringComparison.OrdinalIgnoreCase)
                || entry.Path.EndsWith('/' + fileName, StringComparison.OrdinalIgnoreCase))
            .OrderBy(entry => entry.Path.Count(character => character == '/'))
            .ThenBy(entry => entry.Path.Length)
            .FirstOrDefault();

    /// <summary>建立安全且不区分大小写的 ZIP 条目索引。</summary>
    private static Dictionary<string, IndexedEntry> IndexEntries(ZipArchive archive)
    {
        var entries = new Dictionary<string, IndexedEntry>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in archive.Entries)
        {
            if (string.IsNullOrEmpty(entry.Name)) { continue; }
            var path = NormalizeEntryPath(entry.FullName);
            if (entries.TryAdd(path, new IndexedEntry(path, entry))) { continue; }

            // 重复路径的图片/标注会让“这张图到底用哪份标注”变得不可判定，一律拒绝。
            if (IsImagePath(path) || IsLabelPath(path)) { throw new InvalidDataException($"压缩包包含重复路径：{path}。"); }
            // 描述文件允许重复，但内容必须完全一致。Roboflow 导出的包就会把同一份 data.yaml 写进三个 split，
            // 直接按“重复路径”拒绝会让用户完全无法导入这种再正常不过的数据集。
            if (!HasSameContent(entries[path].Entry, entry)) { throw new InvalidDataException($"压缩包包含内容不一致的重复路径：{path}。"); }
        }
        return entries;
    }

    /// <summary>比较两个条目解压后的内容是否完全一致（仅用于体积极小的描述文件）。</summary>
    private static bool HasSameContent(ZipArchiveEntry left, ZipArchiveEntry right)
    {
        if (left.Length != right.Length || left.Length < 0 || left.Length > MaximumDescriptorBytes) { return false; }
        if (left.Length == 0) { return true; }
        var leftBuffer = new byte[left.Length];
        var rightBuffer = new byte[right.Length];
        using (var leftStream = left.Open()) { leftStream.ReadExactly(leftBuffer); }
        using (var rightStream = right.Open()) { rightStream.ReadExactly(rightBuffer); }
        return leftBuffer.AsSpan().SequenceEqual(rightBuffer);
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

    /// <summary>把 labels 目录下的标注路径还原成不含扩展名的同级图片路径。</summary>
    private static string CorrespondingImageBasePath(string labelPath)
    {
        var segments = labelPath.Split('/');
        var labelDirectoryIndex = Array.FindLastIndex(segments, segment => segment.Equals("labels", StringComparison.OrdinalIgnoreCase));
        if (labelDirectoryIndex < 0) { throw new InvalidDataException($"标注路径不在 labels 目录中：{labelPath}。"); }
        segments[labelDirectoryIndex] = "images";
        segments[^1] = Path.GetFileNameWithoutExtension(segments[^1]);
        return string.Join('/', segments);
    }

    /// <summary>
    /// 拒绝“有标注却没有图片”的包。若同级存在同名但扩展名不受支持的图片，
    /// 则给出可诊断的“格式不支持”提示，而不是让人摸不着头脑的“标注没有对应图片”。
    /// </summary>
    private static void ThrowIfOrphanLabelExists(Dictionary<string, IndexedEntry> entries, HashSet<string> matchedLabels)
    {
        foreach (var label in entries.Values.Where(entry => IsLabelPath(entry.Path) && !matchedLabels.Contains(entry.Path)))
        {
            var prefix = CorrespondingImageBasePath(label.Path) + ".";
            var unsupported = entries.Values.FirstOrDefault(entry => entry.Path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
            if (unsupported is not null)
            {
                throw new InvalidDataException($"压缩包包含不受支持的图片格式：{unsupported.Path}。支持的格式为 jpg、jpeg、png、gif、webp、bmp。");
            }
            throw new InvalidDataException($"标注 {label.Path} 没有对应图片。");
        }
    }

    /// <summary>
    /// 放宽“图片可以没有标注文件”之后必须补上的防呆：
    /// 整包没有一张图片找到标注，而包里又存在与图片同名、只是不在 labels 目录里的 .txt 时，
    /// 几乎一定是标注目录写错了（例如 images/ + annotations/）。这时报错远好于静默导入 0 个标注。
    /// 只按“与图片同名”判定，因此 Roboflow 包里的 README.roboflow.txt 之类不会被误伤。
    /// </summary>
    private static void ThrowIfAnnotationsAreOutsideLabelsDirectory(Dictionary<string, IndexedEntry> entries, List<YoloImportImage> images, int unlabeledImages)
    {
        if (images.Count == 0 || unlabeledImages != images.Count) { return; }
        var imageBaseNames = images
            .Select(image => Path.GetFileNameWithoutExtension(image.ImageEntryPath))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var stray = entries.Values.FirstOrDefault(entry =>
            entry.Path.EndsWith(".txt", StringComparison.OrdinalIgnoreCase)
            && !IsLabelPath(entry.Path)
            && imageBaseNames.Contains(Path.GetFileNameWithoutExtension(entry.Path)));
        if (stray is not null)
        {
            throw new InvalidDataException($"没有任何图片找到对应标注，而 {stray.Path} 与图片同名却不在 labels 目录中：标注必须放在与 images 同级的 labels 目录下。");
        }
    }

    /// <summary>解析 classes.txt，兼容“每行名称”和“索引 名称”两种格式。</summary>
    private static IReadOnlyList<string> ParseClasses(ZipArchiveEntry entry)
    {
        var lines = ReadText(entry, MaximumDescriptorBytes).Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
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

    /// <summary>
    /// 解析 Roboflow / Ultralytics 的 data.yaml 类别表。
    /// 只实现数据集描述需要的 YAML 子集：nc、names 的三种写法（行内列表、行内映射、块列表）。
    /// </summary>
    private static IReadOnlyList<string> ParseDataYamlClasses(string text, string path)
    {
        List<string>? sequence = null;
        SortedDictionary<int, string>? mapping = null;
        int? declaredCount = null;
        var foundNames = false;
        var lines = SplitLines(text);

        for (var lineIndex = 0; lineIndex < lines.Count; lineIndex++)
        {
            var raw = StripComment(lines[lineIndex]).TrimEnd();
            if (raw.Trim().Length == 0) { continue; }
            var (indent, body) = SplitIndent(raw);

            if (TryReadKey(body, "nc", out var countText))
            {
                var trimmedCount = countText.Trim();
                if (!int.TryParse(trimmedCount, NumberStyles.Integer, CultureInfo.InvariantCulture, out var count) || count < 0)
                {
                    throw new InvalidDataException($"{path} 的 nc 不是有效的类别数量：{trimmedCount}。");
                }
                declaredCount = count;
                continue;
            }
            if (!TryReadKey(body, "names", out var inline)) { continue; }

            foundNames = true;
            inline = inline.Trim();
            if (inline.StartsWith('[')) { sequence = ParseFlowSequence(inline, path); }
            else if (inline.StartsWith('{')) { mapping = ParseFlowMapping(inline, path); }
            else if (inline.Length > 0) { sequence = new List<string> { ParseScalar(inline, path) }; }
            else
            {
                // 块形式：names: 之后缩进的 "- 名称"（或 "索引: 名称"）行。
                // 注意 YAML 允许块列表与键同级缩进，因此同级只认 "- " 开头的行。
                var namesIndent = indent;
                for (lineIndex++; lineIndex < lines.Count; lineIndex++)
                {
                    var childRaw = StripComment(lines[lineIndex]).TrimEnd();
                    if (childRaw.Trim().Length == 0) { continue; }
                    var (childIndent, childBody) = SplitIndent(childRaw);
                    if (childBody.StartsWith('-') && (childBody.Length == 1 || char.IsWhiteSpace(childBody[1])))
                    {
                        (sequence ??= new List<string>()).Add(ParseScalar(childBody[1..].Trim(), path));
                        continue;
                    }
                    if (childIndent <= namesIndent) { break; }
                    if (!TryReadIndexedEntry(childBody, out var entryIndex, out var entryName))
                    {
                        throw new InvalidDataException($"{path} 的 names 块包含无法解析的行：{childBody}。");
                    }
                    (mapping ??= new SortedDictionary<int, string>())[entryIndex] = ParseScalar(entryName, path);
                }
            }
            break;
        }

        if (!foundNames) { throw new InvalidDataException($"{path} 缺少 names 类别列表。"); }
        if (sequence is not null && mapping is not null) { throw new InvalidDataException($"{path} 的 names 同时包含列表与映射两种写法。"); }

        IReadOnlyList<string> classes;
        if (mapping is not null)
        {
            classes = mapping.OrderBy(pair => pair.Key)
                .Select((pair, expected) => pair.Key == expected ? pair.Value : throw new InvalidDataException($"{path} 的 names 索引必须从 0 连续递增。"))
                .ToArray();
        }
        else
        {
            classes = sequence ?? throw new InvalidDataException($"{path} 缺少 names 类别列表。");
        }

        if (classes.Count == 0) { throw new InvalidDataException($"{path} 的 names 类别列表为空。"); }
        if (classes.Any(string.IsNullOrWhiteSpace) || classes.Distinct(StringComparer.OrdinalIgnoreCase).Count() != classes.Count)
        {
            throw new InvalidDataException($"{path} 的 names 包含空名称或重复标签。");
        }
        if (declaredCount is int expectedCount && expectedCount != classes.Count)
        {
            throw new InvalidDataException($"{path} 的 nc={expectedCount} 与 names 中的 {classes.Count} 个类别不一致。");
        }
        return classes;
    }

    /// <summary>解析行内列表，例如 <c>['Feathers']</c> 或 <c>["a", "b"]</c>。</summary>
    private static List<string> ParseFlowSequence(string value, string path)
    {
        var end = value.LastIndexOf(']');
        if (end < 0) { throw new InvalidDataException($"{path} 的 names 列表缺少右方括号。"); }
        return SplitFlowItems(value[1..end]).Select(item => ParseScalar(item, path)).ToList();
    }

    /// <summary>解析行内映射，例如 <c>{0: 'a', 1: 'b'}</c>。</summary>
    private static SortedDictionary<int, string> ParseFlowMapping(string value, string path)
    {
        var end = value.LastIndexOf('}');
        if (end < 0) { throw new InvalidDataException($"{path} 的 names 映射缺少右花括号。"); }
        var mapping = new SortedDictionary<int, string>();
        foreach (var item in SplitFlowItems(value[1..end]))
        {
            if (!TryReadIndexedEntry(item, out var index, out var name))
            {
                throw new InvalidDataException($"{path} 的 names 映射包含无法解析的项：{item}。");
            }
            mapping[index] = ParseScalar(name, path);
        }
        return mapping;
    }

    /// <summary>按引号外的逗号切分行内集合。</summary>
    private static List<string> SplitFlowItems(string value)
    {
        var items = new List<string>();
        var quote = '\0';
        var start = 0;
        for (var index = 0; index < value.Length; index++)
        {
            var character = value[index];
            if (quote != '\0')
            {
                if (character == quote) { quote = '\0'; }
                continue;
            }
            if (character is '\'' or '"') { quote = character; continue; }
            if (character == ',') { items.Add(value[start..index]); start = index + 1; }
        }
        items.Add(value[start..]);
        return items.Where(item => item.Trim().Length > 0).ToList();
    }

    /// <summary>去掉行尾注释；只有引号之外的 # 才算注释起始。</summary>
    private static string StripComment(string line)
    {
        var quote = '\0';
        for (var index = 0; index < line.Length; index++)
        {
            var character = line[index];
            if (quote != '\0')
            {
                if (character == quote) { quote = '\0'; }
                continue;
            }
            if (character is '\'' or '"') { quote = character; continue; }
            if (character == '#' && (index == 0 || char.IsWhiteSpace(line[index - 1]))) { return line[..index]; }
        }
        return line;
    }

    /// <summary>拆出行首缩进与正文（YAML 不允许制表符缩进，这里仍然容错处理）。</summary>
    private static (int Indent, string Body) SplitIndent(string line)
    {
        var index = 0;
        while (index < line.Length && line[index] is ' ' or '\t') { index++; }
        return (index, line[index..]);
    }

    /// <summary>识别 <c>键:</c> 或 <c>键: 值</c> 形式，并返回值部分。</summary>
    private static bool TryReadKey(string body, string key, out string value)
    {
        value = string.Empty;
        if (!body.StartsWith(key, StringComparison.Ordinal)) { return false; }
        var rest = body[key.Length..].TrimStart();
        if (rest.Length == 0 || rest[0] != ':') { return false; }
        value = rest[1..];
        return true;
    }

    /// <summary>识别 <c>索引: 名称</c> 形式。</summary>
    private static bool TryReadIndexedEntry(string body, out int index, out string value)
    {
        index = 0;
        value = string.Empty;
        var separator = body.IndexOf(':');
        if (separator <= 0) { return false; }
        if (!int.TryParse(body[..separator].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out index) || index < 0) { return false; }
        value = body[(separator + 1)..];
        return true;
    }

    /// <summary>去掉 YAML 标量两侧的引号；未加引号时按原样返回。</summary>
    private static string ParseScalar(string raw, string path)
    {
        var value = raw.Trim();
        if (value.Length == 0) { return string.Empty; }
        if (value[0] == '\'')
        {
            var end = value.LastIndexOf('\'');
            if (end <= 0) { throw new InvalidDataException($"{path} 的 names 项引号不匹配：{raw}。"); }
            return value[1..end].Replace("''", "'");
        }
        if (value[0] == '"')
        {
            var end = value.LastIndexOf('"');
            if (end <= 0) { throw new InvalidDataException($"{path} 的 names 项引号不匹配：{raw}。"); }
            return value[1..end].Replace("\\\"", "\"").Replace("\\\\", "\\");
        }
        return value;
    }

    /// <summary>按 CRLF / LF / CR 拆行。</summary>
    private static List<string> SplitLines(string text)
        => text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n').ToList();

    /// <summary>解析并验证一个 YOLO 标注文本。</summary>
    private static IReadOnlyList<YoloImportBox> ParseBoxes(ZipArchiveEntry entry, string path, int classCount)
    {
        var text = ReadText(entry, MaximumLabelBytes);
        var boxes = new List<YoloImportBox>();
        var lineNumber = 0;
        foreach (var rawLine in text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            lineNumber++;
            var fields = rawLine.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (fields.Length != 5) { throw new InvalidDataException($"{path} 第 {lineNumber} 行不是 5 列 YOLO 矩形标注。"); }
            if (!int.TryParse(fields[0], NumberStyles.None, CultureInfo.InvariantCulture, out var classIndex) || classIndex < 0 || classIndex >= classCount)
            {
                throw new InvalidDataException($"{path} 第 {lineNumber} 行的类别索引超出类别表范围。");
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
